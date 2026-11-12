using Flowframes.Data;
using Flowframes.IO;
using Flowframes.Ui;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Flowframes.Os
{
    /// <summary> Dados de uma versao publicada no GitHub Releases. </summary>
    public class ReleaseInfo
    {
        public Version Version;
        public string Tag = "";
        public string Notes = "";
        public string PageUrl = Branding.ReleasesUrl;
        public string InstallerUrl = "";
        public string InstallerName = "";
        public long InstallerSize = 0;
    }

    class Updater
    {
        public enum VersionCompareResult { Older, Newer, Equal };

        public static string GetInstalledVerStr()
        {
            Version ver = GetInstalledVer();
            if (ver.Major == 0 && ver.Minor == 0 && ver.Build == 0) return "";
            return ver.ToString();
        }

        public static Version GetInstalledVer()
        {
            try
            {
                var verLines = IoUtils.ReadLines(Paths.GetVerPath());

                if (!verLines.Where(s => s.IsNotEmpty()).Any())
                    return new Version(0, 0, 0);

                return new Version(verLines.First().Trim());
            }
            catch (Exception e)
            {
                Logger.Log("Erro ao ler a versão instalada!", true);
                Logger.Log(e.Message, true);
                return new Version(0, 0, 0);
            }
        }

        public static VersionCompareResult CompareVersions(Version currentVersion, Version newVersion)
        {
            int result = newVersion.CompareTo(currentVersion);

            if (result > 0)
                return VersionCompareResult.Newer;

            if (result < 0)
                return VersionCompareResult.Older;

            return VersionCompareResult.Equal;
        }

        /// <summary>
        /// Consulta a ultima versao publicada no GitHub. Retorna null se nao houver nenhuma release
        /// ou se nao for possivel conectar (sem internet, limite da API, etc.).
        /// </summary>
        public static ReleaseInfo GetLatestRelease()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "FlowFramesBrasil-Updater";
                    client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    client.Encoding = System.Text.Encoding.UTF8;

                    JObject json = JObject.Parse(client.DownloadString(Branding.LatestReleaseApi));

                    if ((bool?)json["draft"] == true || (bool?)json["prerelease"] == true)
                        return null;

                    string tag = (string)json["tag_name"] ?? "";
                    Match m = Regex.Match(tag, @"\d+(\.\d+){1,3}");

                    if (!m.Success)
                        return null;

                    var info = new ReleaseInfo
                    {
                        Version = new Version(m.Value),
                        Tag = tag,
                        Notes = ((string)json["body"] ?? "").Trim(),
                        PageUrl = (string)json["html_url"] ?? Branding.ReleasesUrl
                    };

                    // Procura o instalador (*Setup*.exe) entre os arquivos da release
                    JToken asset = json["assets"]?.FirstOrDefault(a =>
                    {
                        string n = ((string)a["name"] ?? "").ToLowerInvariant();
                        return n.EndsWith(".exe") && n.Contains("setup");
                    });

                    if (asset != null)
                    {
                        info.InstallerUrl = (string)asset["browser_download_url"] ?? "";
                        info.InstallerName = (string)asset["name"] ?? "";
                        info.InstallerSize = (long?)asset["size"] ?? 0;
                    }

                    return info;
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Não foi possível verificar atualizações: {e.Message}", true);
                return null;
            }
        }

        /// <summary>
        /// Baixa o instalador da release para a pasta temporaria e o executa em modo silencioso.
        /// O instalador fecha o app, atualiza os arquivos e abre o programa novamente.
        /// </summary>
        public static async Task<bool> DownloadAndInstallAsync(ReleaseInfo release, Action<int, string> onProgress)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.InstallerUrl))
                return false;

            string dir = Path.Combine(Path.GetTempPath(), "FlowFramesBrasil-Update");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, release.InstallerName.IsEmpty() ? "FlowFramesBrasil-Setup.exe" : release.InstallerName);

            try
            {
                if (File.Exists(file))
                    File.Delete(file);

                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "FlowFramesBrasil-Updater";
                    client.DownloadProgressChanged += (s, e) =>
                    {
                        double mb = e.BytesReceived / 1048576.0;
                        double total = (e.TotalBytesToReceive > 0 ? e.TotalBytesToReceive : release.InstallerSize) / 1048576.0;
                        onProgress?.Invoke(e.ProgressPercentage, $"Baixando atualização... {mb:0.0} / {total:0.0} MB");
                    };

                    await client.DownloadFileTaskAsync(new Uri(release.InstallerUrl), file);
                }

                if (!File.Exists(file) || new FileInfo(file).Length < 1024 * 1024)
                    throw new Exception("Arquivo de instalação inválido ou incompleto.");

                onProgress?.Invoke(100, "Instalando atualização...");

                Process.Start(new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = "/SILENT /CLOSEAPPLICATIONS /NORESTART /RELAUNCH=1",
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception e)
            {
                Logger.Log($"Falha ao baixar/instalar atualização: {e.Message}\n{e.StackTrace}", true);
                try { if (File.Exists(file)) File.Delete(file); } catch { }
                return false;
            }
        }

        /// <summary> Verificacao automatica ao iniciar: atualiza o titulo da janela e avisa se houver versao nova. </summary>
        public static async Task AsyncUpdateCheck()
        {
            Version installed = GetInstalledVer();
            string gpus = OsUtils.GetGpus();
            string windowTitle = Branding.AppName;

            if (installed.ToString() != "0.0.0")
                windowTitle += $" {installed}";
            else
                windowTitle += " [versão desconhecida]";

            if (!gpus.Trim().IsEmpty())
                windowTitle += $" [{gpus}]";

            Program.mainForm.Invoke(() => Program.mainForm.Text = windowTitle);

            if (!Config.GetBool("checkForUpdates", true))
                return;

            ReleaseInfo latest = await Task.Run(() => GetLatestRelease());

            if (latest == null)
            {
                Logger.Log($"Você está usando o {Branding.AppName} {installed}.", true);
                return;
            }

            Logger.Log($"Versão instalada: {installed} - Última versão disponível: {latest.Version}", true);

            if (CompareVersions(installed, latest.Version) != VersionCompareResult.Newer)
                return;

            Logger.Log($"Nova versão disponível: {latest.Version}! Clique em \"Atualizar\" para instalar.");

            Program.mainForm.Invoke(() =>
            {
                string msg = $"Uma nova versão do {Branding.AppName} está disponível!\n\nInstalada: {installed}\nDisponível: {latest.Version}\n\nDeseja ver os detalhes e atualizar agora?";

                if (MessageBox.Show(msg, "Atualização disponível", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    new Flowframes.Forms.UpdaterForm().ShowDialog();
            });
        }

        public static async Task UpdateModelList()
        {
            // Desativado: a lista de modelos acompanha o instalador do Flow Frames Brasil.
            await Task.CompletedTask;
        }
    }
}
