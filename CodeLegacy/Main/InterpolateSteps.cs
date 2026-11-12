using Flowframes.IO;
using System.IO;
using System.Threading.Tasks;
using Flowframes.MiscUtils;
using System.Windows.Forms;
using Flowframes.Ui;

namespace Flowframes.Main
{
    using static Interpolate;

    class InterpolateSteps
    {

        public static async Task Run(string step)
        {
            Logger.Log($"[SBS] Running step '{step}'", true);
            canceled = false;
            Program.mainForm.SetWorking(true);

            currentSettings = Program.mainForm.GetCurrentSettings();
            currentSettings.RefreshAlpha();
            currentSettings.stepByStep = true;

            if (!InterpolateUtils.InputIsValid(currentSettings)) return;     // General input checks
            if (!InterpolateUtils.CheckPathValid(currentSettings.inPath)) return;           // Check if input path/file is valid

            if (step.Contains("Extract Frames"))
                await ExtractFramesStep();

            if (step.Contains("Run Interpolation"))
                await InterpolateStep();

            if (step.Contains("Export"))
                await CreateOutputVid();

            if (step.Contains("Cleanup"))
                await Reset();

            Program.mainForm.SetWorking(false);
            Program.mainForm.SetStatus("Etapa concluída.");
            Logger.Log("Esta etapa foi concluída.");
        }

        public static async Task ExtractFramesStep()
        {
            if (!(await IoUtils.TryDeleteIfExistsAsync(currentSettings.framesFolder)))
            {
                UiUtils.ShowMessageBox("Falha ao apagar a pasta de quadros existente - verifique se nenhum arquivo está aberto em outro programa!", UiUtils.MessageType.Error);
                return;
            }

            await GetFrames();
            await PostProcessFrames(true);
        }

        public static async Task InterpolateStep()
        {
            if (!InterpolateUtils.CheckAiAvailable(currentSettings.ai, currentSettings.model)) return;

            currentSettings.framesFolder = Path.Combine(currentSettings.tempFolder, Paths.framesDir);

            if (IoUtils.GetAmountOfFiles(currentSettings.framesFolder, false, "*") < 2)
            {
                if (Config.GetBool(Config.Key.sbsRunPreviousStepIfNeeded))
                {
                    Logger.Log($"Não há quadros extraídos para interpolar - executando primeiro a etapa de extração...");
                    await ExtractFramesStep();
                }

                if (IoUtils.GetAmountOfFiles(currentSettings.framesFolder, false, "*") < 2)
                {
                    UiUtils.ShowMessageBox("Não há quadros extraídos que possam ser interpolados!\nVocê executou a etapa de extração?", UiUtils.MessageType.Error);
                    return;
                }
            }

            if (!(await IoUtils.TryDeleteIfExistsAsync(currentSettings.interpFolder)))
            {
                UiUtils.ShowMessageBox("Falha ao apagar a pasta de quadros existente - verifique se nenhum arquivo está aberto em outro programa!", UiUtils.MessageType.Error);
                return;
            }

            if (canceled) return;
            Program.mainForm.SetStatus("Executando a IA...");
            await RunAi(currentSettings.interpFolder, currentSettings.ai, true);
            await Task.Run(async () => { await FrameRename.Unrename(); });   // Get timestamps back
            Program.mainForm.SetProgress(0);
        }

        public static async Task CreateOutputVid()
        {
            if (IoUtils.GetAmountOfFiles(currentSettings.interpFolder, false) < 2)
            {
                if (Config.GetBool(Config.Key.sbsRunPreviousStepIfNeeded))
                {
                    Logger.Log($"Não há quadros interpolados para exportar - executando primeiro a etapa de interpolação...");
                    await InterpolateStep();
                }

                if (IoUtils.GetAmountOfFiles(currentSettings.interpFolder, false) < 2)
                {
                    Cancel($"Não há quadros interpolados para codificar!\n\nVocê apagou a pasta?");
                    return;
                }
            }

            string[] outFrames = IoUtils.GetFilesSorted(currentSettings.interpFolder, currentSettings.interpExt);

            if (outFrames.Length > 0 && !IoUtils.CheckImageValid(outFrames[0]))
            {
                UiUtils.ShowMessageBox("Arquivos de quadros inválidos detectados!\n\nSe você usou a codificação automática, isso é normal e não é preciso executar esta etapa, pois o vídeo já foi criado na etapa \"Interpolar\".", UiUtils.MessageType.Error);
                return;
            }

            await Export.ExportFrames(currentSettings.interpFolder, currentSettings.outPath, currentSettings.outSettings, true);
        }

        public static async Task Reset()
        {
            DialogResult dialog = UiUtils.ShowMessageBox($"Tem certeza de que deseja remover todos os arquivos temporários?", "Tem certeza?", MessageBoxButtons.YesNo);

            if (dialog == DialogResult.Yes)
                await Cleanup(true);
        }
    }
}
