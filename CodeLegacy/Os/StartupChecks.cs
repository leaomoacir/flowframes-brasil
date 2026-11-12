using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Flowframes.Forms.Main;
using Flowframes.IO;
using Flowframes.Ui;

namespace Flowframes.Os
{
    class StartupChecks
    {
        public static bool IsAdmin = OsUtils.IsUserAdministrator();

        static bool IsWin10Or11()
        {
            string winVer = OsUtils.GetWindowsVer();

            if (winVer.Trim('?').IsEmpty())
                return true;    // If it fails, return true for future-proofing

            return winVer.Lower().Contains("windows 10") || winVer.Lower().Contains("windows 11");
        }

        public static void CheckOs()
        {
            if (!File.Exists(Paths.GetVerPath()) && Paths.GetExeDir().Lower().Contains("\\temp"))
            {
                UiUtils.ShowMessageBox("Parece que você está executando o programa de dentro de um arquivo compactado.\nExtraia todo o conteúdo primeiro!", UiUtils.MessageType.Error);
                IoUtils.TryDeleteIfExists(Paths.GetDataPath());
                Application.Exit();
            }

            // Attempt to create an empty new folder in exe dir to check if we have permissions
            try
            {
                string testDir = Path.Combine(Paths.GetExeDir(), "test.tmp");
                Directory.CreateDirectory(Path.Combine(Paths.GetExeDir(), testDir));
                Directory.Delete(Path.Combine(Paths.GetExeDir(), testDir));
            }
            catch (Exception e)
            {
                UiUtils.ShowMessageBox($"O programa não tem permissão para gravar na própria pasta!\nMova-o para outra pasta.\n\nPasta de instalação atual: {Paths.GetExeDir()}", UiUtils.MessageType.Error);
                Application.Exit();
            }

            string winVer = OsUtils.GetWindowsVerVerbose(out string winVerDesc);
            Logger.Log($"[OS] Running {winVer} ({winVerDesc.Replace("Windows ", "")}) [{(IsAdmin ? "Admin" : "User")}]", true);

            if (!Environment.Is64BitOperatingSystem && !Config.GetBool("allow32Bit", false))
            {
                UiUtils.ShowMessageBox("Este aplicativo não é compatível com sistemas operacionais de 32 bits!", UiUtils.MessageType.Error);
                Application.Exit();
            }

            if (winVer.IsEmpty())
                return;

            if (!winVer.Lower().Contains("windows 10") && !winVer.Lower().Contains("windows 11") && !Config.GetBool("ignoreIncompatibleOs", false))
            {
                UiUtils.ShowMessageBox($"Este aplicativo foi feito para Windows 10/11 e não é oficialmente compatível com {winVer}.\n\n" +
                                $"Use por sua conta e risco; não há suporte enquanto você estiver no {winVer}.", UiUtils.MessageType.Warning);
            }
        }

        public static async Task SymlinksCheck()
        {
            if (!IsWin10Or11())
                return;

            bool silent = Config.GetBool("silentDevmodeCheck", true);
            string ver = Updater.GetInstalledVer().ToString();
            bool symlinksAllowed = Symlinks.SymlinksAllowed();
            Logger.Log($"Symlink creation is {(symlinksAllowed ? "allowed." : $"not allowed!")} (Admin: {IsAdmin} - Dev Mode: {OsUtils.GetDeveloperModeState()})", true);

            if (!symlinksAllowed && Config.Get(Config.Key.askedForDevModeVersion) != ver)
            {
                if (!silent)
                {
                    UiUtils.ShowMessageBox("O programa vai ativar o Modo de Desenvolvedor do Windows, necessário para melhorar a codificação de vídeo.\n\nIsso exige privilégios de administrador uma vez.", UiUtils.MessageType.Message);
                }

                Logger.Log($"Trying to enable dev mode.", true);

                string devmodeBatchPath = Path.Combine(Paths.GetDataPath(), "devmode.bat");
                File.WriteAllText(devmodeBatchPath, Properties.Resources.devmode);

                Process devmodeProc = OsUtils.NewProcess(true);
                devmodeProc.StartInfo.Arguments = $"/C {devmodeBatchPath.Wrap()}";
                devmodeProc.Start();
                while (!devmodeProc.HasExited) await Task.Delay(100);

                bool symlinksWorksNow = false;

                for (int retries = 8; retries > 0; retries--)
                {
                    symlinksWorksNow = Symlinks.SymlinksAllowed();

                    if (symlinksWorksNow)
                        break;

                    await Task.Delay(500);
                }

                if (!symlinksWorksNow)
                {
                    if (!silent)
                    {
                        UiUtils.ShowMessageBox("Falha ao ativar o Modo de Desenvolvedor - talvez você não tenha privilégios suficientes.\n\nSem o Modo de Desenvolvedor, a codificação de vídeo será visivelmente mais lenta.\n\nVocê pode ativá-lo manualmente nas Configurações do Windows:\nConfigurações -> Atualização e Segurança -> Para desenvolvedores -> Modo de Desenvolvedor.", UiUtils.MessageType.Message);
                    }

                    Logger.Log("Failed to enable dev mode.", true);
                    Config.Set("askedForDevModeVersion", ver);
                }
                else
                {
                    Logger.Log("Modo de Desenvolvedor do Windows ativado.", silent);
                }

                IoUtils.TryDeleteIfExists(devmodeBatchPath);
            }
        }

        public static void GpuCheck(bool exit = true)
        {
            // Sem GPU o programa continua funcionando: as versoes NCNN processam na CPU (bem mais lento).
            if (!OsUtils.HasGpu())
            {
                Logger.Log("Nenhuma GPU compatível foi detectada. O programa vai usar a CPU - funciona, mas é bem mais lento. " +
                           "Se você tem uma placa de vídeo, atualize o driver (Nvidia/AMD/Intel).");
            }

            Logger.Log(ProcessingDevice.Summary());
        }
    }
}
