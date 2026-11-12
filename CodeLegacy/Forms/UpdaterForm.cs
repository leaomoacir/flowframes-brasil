using Flowframes.Data;
using Flowframes.Os;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Flowframes.Forms
{
    public partial class UpdaterForm : Form
    {
        Version installed;
        ReleaseInfo latest;
        bool busy = false;

        public UpdaterForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            InitializeComponent();
        }

        private async void UpdaterForm_Load(object sender, EventArgs e)
        {
            installed = Updater.GetInstalledVer();
            installedLabel.Text = installed.ToString();
            latestLabel.Text = "Verificando...";
            statusLabel.Text = "Consultando o GitHub...";
            updatePatreonBtn.Enabled = false;
            updateFreeBtn.Enabled = false;

            latest = await Task.Run(() => Updater.GetLatestRelease());

            if (latest == null)
            {
                latestLabel.Text = "-";
                statusLabel.Text = "Não foi possível verificar agora.\nConfira sua conexão com a internet.";
                updateFreeBtn.Enabled = true;
                return;
            }

            latestLabel.Text = latest.Version.ToString();
            updateFreeBtn.Enabled = true;

            switch (Updater.CompareVersions(installed, latest.Version))
            {
                case Updater.VersionCompareResult.Newer:
                    statusLabel.Text = string.IsNullOrWhiteSpace(latest.InstallerUrl)
                        ? "Há uma nova versão! Baixe-a pela página de novidades."
                        : "Há uma nova versão disponível!";
                    updatePatreonBtn.Enabled = !string.IsNullOrWhiteSpace(latest.InstallerUrl);
                    break;
                case Updater.VersionCompareResult.Equal:
                    statusLabel.Text = "Você já está com a versão mais recente.";
                    break;
                default:
                    statusLabel.Text = "Você está usando uma versão de testes (mais nova que a publicada).";
                    break;
            }
        }

        float lastProg = -1f;
        public void SetProgLabel(float prog, string str)
        {
            if (prog == lastProg) return;
            lastProg = prog;
            downloadingLabel.Text = str;
        }

        // Botao da esquerda: baixar e instalar
        private async void updatePatreonBtn_Click(object sender, EventArgs e)
        {
            if (busy || latest == null) return;

            busy = true;
            updatePatreonBtn.Enabled = false;
            updateFreeBtn.Enabled = false;

            bool ok = await Updater.DownloadAndInstallAsync(latest, (pct, text) =>
            {
                if (IsHandleCreated)
                    BeginInvoke(new Action(() => SetProgLabel(pct, text)));
            });

            if (ok)
            {
                // O instalador fecha o programa e o reabre ao terminar.
                Application.Exit();
                return;
            }

            busy = false;
            downloadingLabel.Text = "Falha no download. Tente novamente ou use a página de novidades.";
            updatePatreonBtn.Enabled = true;
            updateFreeBtn.Enabled = true;
        }

        // Botao da direita: abrir pagina da versao (novidades)
        private void updateFreeBtn_Click(object sender, EventArgs e)
        {
            Process.Start(latest != null ? latest.PageUrl : Branding.ReleasesUrl);
        }
    }
}
