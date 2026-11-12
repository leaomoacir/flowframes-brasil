using Flowframes.Media;
using Flowframes.Data;
using Flowframes.Forms;
using Flowframes.IO;
using Flowframes.Os;
using Flowframes.Ui;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using I = Flowframes.Interpolate;
using Padding = Flowframes.Data.Padding;

namespace Flowframes.Main
{
    class InterpolateUtils
    {
        public static async Task CopyLastFrame(int lastFrameNum)
        {
            if (I.canceled) return;

            try
            {
                lastFrameNum--; // We have to do this as extracted frames start at 0, not 1
                bool frameFolderInput = IoUtils.IsPathDirectory(I.currentSettings.inPath);
                string targetPath = Path.Combine(I.currentSettings.framesFolder, lastFrameNum.ToString().PadLeft(Padding.inputFrames, '0') + I.currentSettings.framesExt);
                if (File.Exists(targetPath)) return;

                Size res = IoUtils.GetImage(IoUtils.GetFilesSorted(I.currentSettings.framesFolder, false).First()).Size;

                if (frameFolderInput)
                {
                    string lastFramePath = IoUtils.GetFilesSorted(I.currentSettings.inPath, false).Last();
                    await FfmpegExtract.ExtractLastFrame(lastFramePath, targetPath, res);
                }
                else
                {
                    await FfmpegExtract.ExtractLastFrame(I.currentSettings.inPath, targetPath, res);
                }
            }
            catch (Exception e)
            {
                Logger.Log("CopyLastFrame Error: " + e.Message);
            }
        }

        public static int GetProgressWaitTime(int numFrames)
        {
            float hddMultiplier = !Program.lastInputPathIsSsd ? 2f : 1f;

            int waitMs = 200;

            if (numFrames > 100)
                waitMs = 500;

            if (numFrames > 1000)
                waitMs = 1000;

            if (numFrames > 2500)
                waitMs = 1500;

            if (numFrames > 5000)
                waitMs = 2500;

            return (waitMs * hddMultiplier).RoundToInt();
        }

        public static string GetTempFolderLoc(string inPath, string outPath)
        {
            string basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "Flowframes");
            int tempFolderLoc = Config.GetInt(Config.Key.tempFolderLoc);

            switch (tempFolderLoc)
            {
                case 1:
                    basePath = inPath.GetParentDir();
                    break;

                case 2:
                    basePath = outPath;
                    break;

                case 3:
                    basePath = Paths.GetSessionDataPath();
                    break;

                case 4:
                    string custPath = Config.Get(Config.Key.tempDirCustom);
                    if (IoUtils.IsDirValid(custPath))
                    {
                        basePath = custPath;
                    }
                    break;
            }

            string folderName = Path.GetFileNameWithoutExtension(inPath).StripBadChars().Remove(" ").Trunc(35, false) + "_tmp";
            return Path.Combine(basePath, folderName);
        }

        public static bool InputIsValid(InterpSettings s)
        {
            try
            {
                bool passes = true;
                bool isFile = !IoUtils.IsPathDirectory(s.inPath);

                if (passes && (IoUtils.IsPathOneDrive(s.inPath) || IoUtils.IsPathOneDrive(s.outPath)))
                {
                    UiUtils.ShowMessageBox("Caminhos do OneDrive não são suportados. Use uma pasta local.");
                    passes = false;
                }

                if ((passes && (isFile && !IoUtils.IsFileValid(s.inPath)) || (!isFile && !IoUtils.IsDirValid(s.inPath))))
                {
                    UiUtils.ShowMessageBox("O caminho de entrada não é válido!");
                    passes = false;
                }

                if (passes && !IoUtils.IsDirValid(s.outPath))
                {
                    UiUtils.ShowMessageBox("O caminho de saída não é válido!");
                    passes = false;
                }

                if (passes && (s.tempFolder.StartsWith(@"\\") || IoUtils.IsPathOneDrive(s.tempFolder)))
                {
                    UiUtils.ShowMessageBox("O programa não aceita caminhos de rede como pasta temporária!\nUse uma pasta local.");
                    passes = false;
                }

                float fpsLimit = s.outFpsResampled.Float;
                int maxEncoderFps = s.outSettings.Encoder.GetInfo().MaxFramerate;

                if (passes && (s.outFps.Float < 1f || (s.outFps.Float > maxEncoderFps && !(fpsLimit > 0 && fpsLimit <= maxEncoderFps))))
                {
                    string imgSeqNote = isFile ? "" : "\n\nWhen using an image sequence as input, you always have to specify the frame rate manually.";
                    UiUtils.ShowMessageBox($"Taxa de quadros de saída inválida ({s.outFps.Float}).\nDeve estar entre 1 e {maxEncoderFps}. Reduza o fator de interpolação ou use a opção \"Taxa de quadros máxima da saída\".{imgSeqNote}");
                    passes = false;
                }

                if (passes && s.dedupe && I.currentMediaFile.IsVfr)
                {
                    UiUtils.ShowMessageBox($"Usar a remoção de duplicatas em vídeos VFR não é suportado no momento.\n\nVá em Configurações rápidas e desative a remoção de duplicatas ou desative o VFR.");
                    passes = false;
                }

                if(passes && I.currentMediaFile.VideoExtraData.IsHdr)
                {
                    string badHdrSettingsStr = I.currentSettings.outSettings.GetHdrNotSuitableReason();

                    if(badHdrSettingsStr.IsNotEmpty())
                    {
                        Logger.Log(badHdrSettingsStr);
                    }
                }

                I.InterpProgressMultiplier = s.FpsResampling ? s.outFps.Float / fpsLimit : 1f;

                if (!passes)
                    I.Cancel("Configurações inválidas detectadas.", true);

                return passes;
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to run InputIsValid: {e.Message}\n{e.StackTrace}", true);
                return false;
            }
        }

        public static bool CheckAiAvailable(AiInfo ai, ModelCollection.ModelInfo model, bool allowNullModel = false)
        {
            if (IoUtils.GetAmountOfFiles(Path.Combine(Paths.GetPkgPath(), ai.PkgDir), true) < 1)
            {
                UiUtils.ShowMessageBox("A IA selecionada não está instalada!", UiUtils.MessageType.Error);
                I.Cancel("IA selecionada indisponível.", true);
                return false;
            }

            if (!allowNullModel && (model == null || model.Dir.Trim() == ""))
            {
                UiUtils.ShowMessageBox("Nenhum modelo de IA válido foi selecionado!", UiUtils.MessageType.Error);
                I.Cancel("Nenhum modelo válido selecionado.", true);
                return false;
            }

            if (I.currentSettings.ai.NameInternal.Upper().Contains("CUDA") && NvApi.NvGpus.Count < 1)
            {
                UiUtils.ShowMessageBox("Aviso: nenhuma GPU Nvidia foi detectada. O CUDA pode usar a CPU!\n\nTente uma implementação NCNN se você não tem uma GPU Nvidia.", UiUtils.MessageType.Error);

                if (!Config.GetBool("allowCudaWithoutDetectedGpu", true))
                {
                    I.Cancel("Nenhuma placa de vídeo compatível com CUDA disponível.", true);
                    return false;
                }
            }

            return true;
        }

        public static bool CheckDeleteOldTempFolder()
        {
            if (!IoUtils.TryDeleteIfExists(I.currentSettings.tempFolder))
            {
                UiUtils.ShowMessageBox("Falha ao remover uma pasta temporária existente deste vídeo!\nVerifique se nenhum quadro está aberto em um editor.", UiUtils.MessageType.Error);
                I.Cancel();
                return false;
            }
            return true;
        }

        public static bool CheckPathValid(string path)
        {
            if (path.StartsWith(@"\\"))
            {
                UiUtils.ShowMessageBox("O caminho de entrada não é válido.\nCaminhos UNC/de rede não são suportados.");
                I.Cancel();
                return false;
            }

            bool isDir = IoUtils.IsPathDirectory(path);

            if (isDir && !IoUtils.IsDirValid(path))
            {
                UiUtils.ShowMessageBox("A pasta de entrada não é válida.\nVerifique se ela ainda existe e não foi renomeada ou movida!");
                I.Cancel();
                return false;
            }

            if (!isDir && !IoUtils.IsFileValid(path))
            {
                UiUtils.ShowMessageBox("O arquivo de vídeo de entrada não é válido.\nVerifique se ele ainda existe e não foi renomeado ou movido!");
                return false;
            }

            return true;
        }

        public static async Task<Size> GetOutputResolution(FfmpegCommands.ModuloMode moduloMode, string inputPath, bool print = false)
        {
            Size resolution = await GetMediaResolutionCached.GetSizeAsync(inputPath);
            return GetInterpolationResolution(moduloMode, resolution, print);
        }

        public static Size GetInterpolationResolution(FfmpegCommands.ModuloMode moduloMode, Size inputRes, bool onlyRoundUp = true, bool print = false)
        {
            Size res = new Size(inputRes.Width, inputRes.Height);
            int maxHeight = Config.GetInt(Config.Key.maxVidHeight);
            int modulo = FfmpegCommands.GetModulo(moduloMode);
            float factor = res.Height > maxHeight ? (float)maxHeight / res.Height : 1f; // Calculate downscale factor if bigger than max, otherwise just use 1x
            int width = RoundDivisibleBy((res.Width * factor).RoundToInt(), modulo, onlyRoundUp);
            int height = RoundDivisibleBy((res.Height * factor).RoundToInt(), modulo, onlyRoundUp);
            res = new Size(width, height);

            if (print && factor < 1f)
                Logger.Log($"O vídeo é maior que o máximo - reduzindo para {width}x{height}.");

            if(inputRes != res)
                Logger.Log($"Scaled input resolution {inputRes.Width}x{inputRes.Height} to {res.Width}x{res.Height} (Modulo: {modulo}/{moduloMode})", true);

            return res;
        }

        public static int RoundDivisibleBy(float number, int divisibleBy, bool onlyRoundUp = false)
        {
            int numberInt = number.RoundToInt();

            if (divisibleBy == 0)
                return numberInt;

            return onlyRoundUp ? (int)Math.Ceiling((double)number / divisibleBy) * divisibleBy : (int)Math.Round((double)number / divisibleBy) * divisibleBy;
        }

        public static bool CanUseAutoEnc(bool stepByStep, InterpSettings current)
        {
            AutoEncode.UpdateChunkAndBufferSizes();

            if (current.ai.Piped)
            {
                Logger.Log($"Auto-Encode is off: Not applicable for piped encoding.", true, toConsole: false);
                return false;
            }

            if (current.outSettings.Format == Enums.Output.Format.Gif)
            {
                Logger.Log($"Auto-Encode is off: Not supported for {current.outSettings.Format.ToString().Upper()} output.", true);
                return false;
            }

            if (stepByStep && !Config.GetBool(Config.Key.sbsAllowAutoEnc))
            {
                Logger.Log($"Auto-Encode is off: Using step-by-step mode, but {nameof(Config.Key.sbsAllowAutoEnc)} is false.", true);
                return false;
            }

            if (!stepByStep && Config.GetInt(Config.Key.autoEncMode) == 0)
            {
                Logger.Log($"Auto-Encode is off: Disabled in settings ({nameof(Config.Key.autoEncMode)}).", true);
                return false;
            }

            int inFrames = IoUtils.GetAmountOfFiles(current.framesFolder, false);
            if (inFrames * current.interpFactor < (AutoEncode.chunkSize + AutoEncode.safetyBufferFrames) * 1.2f)
            {
                Logger.Log($"Auto-Encode is off: Not enough frames for chunking.", true);
                return false;
            }

            return true;
        }

        public static bool UseUhd(Size? outputRes = null)
        {
            if (outputRes == null)
                outputRes = I.currentSettings.OutputResolution;
            return outputRes.Value.Height >= Config.GetInt(Config.Key.uhdThresh);
        }

        public static void FixConsecutiveSceneFrames(string sceneFramesPath, string sourceFramesPath)
        {
            if (!Directory.Exists(sceneFramesPath) || IoUtils.GetAmountOfFiles(sceneFramesPath, false) < 1)
                return;

            List<string> sceneFrames = IoUtils.GetFilesSorted(sceneFramesPath).Select(x => Path.GetFileNameWithoutExtension(x)).ToList();
            List<string> sourceFrames = IoUtils.GetFilesSorted(sourceFramesPath).Select(x => Path.GetFileNameWithoutExtension(x)).ToList();
            List<string> sceneFramesToDelete = new List<string>();

            foreach (string scnFrame in sceneFrames)
            {
                if (sceneFramesToDelete.Contains(scnFrame))
                    continue;

                int sourceIndexForScnFrame = sourceFrames.IndexOf(scnFrame); // Get source index of scene frame
                if ((sourceIndexForScnFrame + 1) == sourceFrames.Count)
                    continue;
                string followingFrame = sourceFrames[sourceIndexForScnFrame + 1];       // Get filename/timestamp of the next source frame

                if (sceneFrames.Contains(followingFrame))                               // If next source frame is in scene folder, add to deletion list
                    sceneFramesToDelete.Add(followingFrame);
            }

            foreach (string frame in sceneFramesToDelete)
                IoUtils.TryDeleteIfExists(Path.Combine(sceneFramesPath, frame + I.currentSettings.framesExt));
        }

        public static int GetRoundedInterpFramesPerInputFrame(float factor, bool roundDown = true)
        {
            if (roundDown)
                return (int)Math.Floor(factor) - 1;
            else
                return factor.RoundToInt();
        }

        public static Fraction AskForFramerate(string mediaName, bool isImageSequence = true)
        {
            string text = $"Informe a taxa de quadros original (antes da interpolação) de{(isImageSequence ? " the image sequence" : "")} '{mediaName.Trunc(80)}'.";
            var form = new PromptForm("Informar taxa de quadros", text, "15");
            form.ShowDialog();
            return new Fraction(form.EnteredText);
        }
    }
}
