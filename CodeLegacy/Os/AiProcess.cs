using Flowframes.Data;
using Flowframes.IO;
using Flowframes.Main;
using Flowframes.MiscUtils;
using Flowframes.Ui;
using Flowframes.Utilities;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Paths = Flowframes.IO.Paths;

namespace Flowframes.Os
{
    class AiProcess
    {
        public static bool hasShownError;
        public static string logName;
        public static Process lastAiProcess;
        public static Stopwatch processTime = new Stopwatch();
        public static Stopwatch processTimeMulti = new Stopwatch();

        public static int lastStartupTimeMs = 1000;
        private static string lastInPath;
        private static string NcnnGpuIds => ProcessingDevice.EffectiveNcnnIds();

        public static void Kill()
        {
            if (lastAiProcess == null) return;

            try
            {
                AiProcessSuspend.SetRunning(false);
                OsUtils.KillProcessTree(lastAiProcess.Id);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to kill process tree: {e.Message}", true);
            }
        }

        static void AiStarted(Process proc, int startupTimeMs, string inPath = "")
        {
            lastStartupTimeMs = startupTimeMs;
            processTime.Restart();
            lastAiProcess = proc;
            AiProcessSuspend.SetRunning(true);
            lastInPath = string.IsNullOrWhiteSpace(inPath) ? Interpolate.currentSettings.framesFolder : inPath;
            hasShownError = false;
        }

        static void AiStartedRt(Process proc, string inPath = "")
        {
            lastAiProcess = proc;
            AiProcessSuspend.SetRunning(true);
            lastInPath = string.IsNullOrWhiteSpace(inPath) ? Interpolate.currentSettings.framesFolder : inPath;
            hasShownError = false;
        }

        static void SetProgressCheck(string interpPath, float factor)
        {
            int frames = IoUtils.GetAmountOfFiles(lastInPath, false);
            int target = ((frames * factor) - (factor - 1)).RoundToInt();
            InterpolationProgress.progressPaused = false;
            InterpolationProgress.currentFactor = factor;

            if (InterpolationProgress.progCheckRunning)
                InterpolationProgress.targetFrames = target;
            else
                InterpolationProgress.GetProgressByFrameAmount(interpPath, target);
        }

        static void SetProgressCheck(int sourceFrames, float factor, string logFile)
        {
            int target = ((sourceFrames * factor) - (factor - 1)).RoundToInt();
            InterpolationProgress.progressPaused = false;
            InterpolationProgress.currentFactor = factor;

            if (InterpolationProgress.progCheckRunning)
                InterpolationProgress.targetFrames = target;
            else
                InterpolationProgress.GetProgressFromFfmpegLog(logFile, target);
        }

        static async Task AiFinished(string aiName, bool rt = false)
        {
            if (Interpolate.canceled) return;
            Program.mainForm.SetProgress(100);
            AiProcessSuspend.SetRunning(false);

            if (rt)
            {
                Logger.Log($"{aiName} interrompido.");
                lastAiProcess = null;
                return;
            }

            int interpFramesFiles = IoUtils.GetAmountOfFiles(Interpolate.currentSettings.interpFolder, false, "*" + Interpolate.currentSettings.interpExt);
            int interpFramesCount = interpFramesFiles + InterpolationProgress.deletedFramesCount;

            if (!Interpolate.currentSettings.ai.Piped)
                InterpolationProgress.UpdateInterpProgress(interpFramesCount, InterpolationProgress.targetFrames);

            string logStr = $"Done running {aiName} - Interpolation took {FormatUtils.Time(processTime.Elapsed)}.";

            if (InterpolationProgress.LastFps > 0.0001)
            {
                logStr += $" Output FPS: {InterpolationProgress.LastFps.ToString("0.0")}";
            }

            if (Interpolate.currentlyUsingAutoEnc && AutoEncode.HasWorkToDo())
            {
                logStr += " - Waiting for encoding to finish...";
                Program.mainForm.SetStatus("Criando o vídeo de saída a partir dos quadros...");
            }

            if (Interpolate.currentSettings.outSettings.Format != Enums.Output.Format.Realtime)
                Logger.Log(logStr, replaceLastLine: Logger.LastUiLine.Contains("FPS"));

            processTime.Stop();

            if (!Interpolate.currentSettings.ai.Piped && interpFramesCount < 3)
            {
                string amount = interpFramesCount > 0 ? $"Apenas {interpFramesCount}" : "Nenhum";

                if (logName.IsEmpty())
                {
                    Interpolate.Cancel($"A interpolação falhou - {amount} quadros interpolados foram criados e nenhum log foi gravado." + ErrorFooter(Interpolate.currentSettings.ai, ""));
                    return;
                }

                string[] logLines = File.ReadAllLines(Path.Combine(Paths.GetLogPath(), logName + ".txt"));
                string log = string.Join("\n", logLines.Reverse().Take(10).Reverse().Select(x => x.Split("]: ").Last()).ToList());
                Interpolate.Cancel($"A interpolação falhou - {amount} quadros interpolados foram criados.\n\n\nÚltimas 10 linhas do log:\n{log}\n\nConsulte o log '{logName}' para mais detalhes." + ErrorFooter(Interpolate.currentSettings.ai, string.Join("\n", logLines.Reverse().Take(80))));
                return;
            }

            try
            {
                while (Interpolate.currentlyUsingAutoEnc && Program.busy)
                {
                    if (AvProcess.lastAvProcess != null && !AvProcess.lastAvProcess.HasExited)
                    {
                        if (Logger.LastLogLine.Contains("frame: "))
                            Logger.Log(FormatUtils.BeautifyFfmpegStats(Logger.LastLogLine), false, Logger.LastUiLine.Contains("frame"));
                    }

                    if (AvProcess.lastAvProcess.HasExited && !AutoEncode.HasWorkToDo())     // Stop logging if ffmpeg is not running & AE is done
                        break;

                    await Task.Delay(500);
                }
            }
            catch (Exception e)
            {
                Logger.Log($"AiFinished encoder logging error: {e.Message}\n{e.StackTrace}", true);
            }

            lastAiProcess = null;
        }

        public static async Task RunRifeCuda(string framesPath, float interpFactor, string mdl)
        {
            AiInfo ai = Implementations.rifeCuda;

            if (Interpolate.currentlyUsingAutoEnc)      // Ensure AutoEnc is not paused
                AutoEncode.paused = false;

            try
            {
                string rifeDir = Path.Combine(Paths.GetPkgPath(), ai.PkgDir);
                string script = "rife.py";

                if (!File.Exists(Path.Combine(rifeDir, script)))
                {
                    Interpolate.Cancel("Script do RIFE não encontrado! Verifique se você não modificou nenhum arquivo.");
                    return;
                }

                string archFilesDir = Path.Combine(rifeDir, "arch");
                string archFilesDirModel = Path.Combine(rifeDir, mdl, "arch");

                if (Directory.Exists(archFilesDirModel))
                {
                    Logger.Log($"Model {mdl} has architecture python files - copying to arch.", true);
                    IoUtils.DeleteContentsOfDir(archFilesDir);
                    IoUtils.CopyDir(archFilesDirModel, archFilesDir);
                }

                await RunRifeCudaProcess(framesPath, Paths.interpDir, script, interpFactor, mdl);
            }
            catch (Exception e)
            {
                Logger.Log($"Erro ao executar {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        public static async Task RunRifeCudaProcess(string inPath, string outDir, string script, float interpFactor, string mdl)
        {
            string outPath = Path.Combine(inPath.GetParentDir(), outDir);
            Directory.CreateDirectory(outPath);
            string uhdStr = InterpolateUtils.UseUhd() ? "--UHD" : "";
            string wthreads = $"--wthreads {2 * (int)interpFactor}";
            string rbuffer = $"--rbuffer {Config.GetInt(Config.Key.rifeCudaBufferSize, 200)}";
            //string scale = $"--scale {Config.GetFloat("rifeCudaScale", 1.0f).ToString()}";
            string prec = Config.GetBool(Config.Key.rifeCudaFp16) ? "--fp16" : "";
            string args = $" --input {inPath.Wrap()} --output {outDir} --model {mdl} --multi {interpFactor} {uhdStr} {wthreads} {rbuffer} {prec}";

            Process rifePy = OsUtils.NewProcess(true);
            AiStarted(rifePy, 3500);
            SetProgressCheck(Path.Combine(Interpolate.currentSettings.tempFolder, outDir), interpFactor);
            rifePy.StartInfo.Arguments = $"/C cd /D {Path.Combine(Paths.GetPkgPath(), Implementations.rifeCuda.PkgDir).Wrap()} & " +
                $"set CUDA_VISIBLE_DEVICES={Config.Get(Config.Key.torchGpus)} & {Python.GetPyCmd()} {script} {args}";
            Logger.Log($"Executando RIFE (CUDA){(InterpolateUtils.UseUhd() ? " (modo UHD)" : "")}...", false);
            Logger.Log("Dispositivo: " + ProcessingDevice.DescribeCuda());
            Logger.Log("cmd.exe " + rifePy.StartInfo.Arguments, true);

            rifePy.OutputDataReceived += (sender, outLine) => { LogOutput(outLine.Data, Implementations.rifeCuda); };
            rifePy.ErrorDataReceived += (sender, outLine) => { LogOutput("[E] " + outLine.Data, Implementations.rifeCuda, true); };
            rifePy.Start();
            rifePy.BeginOutputReadLine();
            rifePy.BeginErrorReadLine();

            while (!rifePy.HasExited) await Task.Delay(1);
        }

        public static async Task RunFlavrCuda(string framesPath, float interpFactor, string mdl)
        {
            AiInfo ai = Implementations.flavrCuda;

            if (Interpolate.currentlyUsingAutoEnc)      // Ensure AutoEnc is not paused
                AutoEncode.paused = false;

            try
            {
                string flavDir = Path.Combine(Paths.GetPkgPath(), ai.PkgDir);
                string script = "flavr.py";

                if (!File.Exists(Path.Combine(flavDir, script)))
                {
                    Interpolate.Cancel("Script do FLAVR não encontrado! Verifique se você não modificou nenhum arquivo.");
                    return;
                }

                await RunFlavrCudaProcess(framesPath, Paths.interpDir, script, interpFactor, mdl);
            }
            catch (Exception e)
            {
                Logger.Log($"Erro ao executar {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        public static async Task RunFlavrCudaProcess(string inPath, string outDir, string script, float interpFactor, string mdl)
        {
            string outPath = Path.Combine(inPath.GetParentDir(), outDir);
            Directory.CreateDirectory(outPath);
            string args = $" --input {inPath.Wrap()} --output {outPath.Wrap()} --model {mdl}/{mdl}.pth --factor {interpFactor}";

            Process flavrPy = OsUtils.NewProcess(true);
            AiStarted(flavrPy, 4000);
            SetProgressCheck(Path.Combine(Interpolate.currentSettings.tempFolder, outDir), interpFactor);
            flavrPy.StartInfo.Arguments = $"/C cd /D {Path.Combine(Paths.GetPkgPath(), Implementations.flavrCuda.PkgDir).Wrap()} & " +
                $"set CUDA_VISIBLE_DEVICES={Config.Get(Config.Key.torchGpus)} & {Python.GetPyCmd()} {script} {args}";
            Logger.Log($"Executando FLAVR (CUDA)...", false);
            Logger.Log("Dispositivo: " + ProcessingDevice.DescribeCuda());
            Logger.Log("cmd.exe " + flavrPy.StartInfo.Arguments, true);

            flavrPy.OutputDataReceived += (sender, outLine) => { LogOutput(outLine.Data, Implementations.flavrCuda); };
            flavrPy.ErrorDataReceived += (sender, outLine) => { LogOutput("[E] " + outLine.Data, Implementations.flavrCuda, true); };
            flavrPy.Start();
            flavrPy.BeginOutputReadLine();
            flavrPy.BeginErrorReadLine();

            while (!flavrPy.HasExited) await Task.Delay(1);
        }

        public static async Task RunRifeNcnn(string framesPath, string outPath, float factor, string mdl)
        {
            AiInfo ai = Implementations.rifeNcnn;
            processTimeMulti.Restart();

            try
            {
                Logger.Log($"Executando RIFE (NCNN){(InterpolateUtils.UseUhd() ? " (modo UHD)" : "")}...", false);
                Logger.Log("Dispositivo: " + ProcessingDevice.DescribeNcnn());

                await RunRifeNcnnProcess(framesPath, factor, outPath, mdl);
                await NcnnUtils.DeleteNcnnDupes(outPath, factor);
            }
            catch (Exception e)
            {
                Logger.Log($"Erro ao executar {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        static async Task RunRifeNcnnProcess(string inPath, float factor, string outPath, string mdl)
        {
            Directory.CreateDirectory(outPath);
            string logFileName = "rife-ncnn-log";
            Process rifeNcnn = OsUtils.NewProcess(true);
            AiStarted(rifeNcnn, 1500, inPath);
            SetProgressCheck(outPath, factor);
            int targetFrames = ((IoUtils.GetAmountOfFiles(lastInPath, false, "*.*") * factor).RoundToInt()); // TODO: Maybe won't work with fractional factors ??

            string frames = mdl.Contains("v4") ? $"-n {targetFrames}" : "";
            string uhdStr = InterpolateUtils.UseUhd() ? "-u" : "";
            string ttaStr = Config.GetBool(Config.Key.rifeNcnnUseTta, false) ? "-x" : "";

            rifeNcnn.StartInfo.Arguments = $"/C cd /D {Path.Combine(Paths.GetPkgPath(), Implementations.rifeNcnn.PkgDir).Wrap()} & rife-ncnn-vulkan.exe " +
                $" -v -i {inPath.Wrap()} -o {outPath.Wrap()} {frames} -m {mdl.Lower()} {ttaStr} {uhdStr} -g {NcnnGpuIds} -f {NcnnUtils.GetNcnnPattern()} -j {NcnnUtils.GetNcnnThreads(Implementations.rifeNcnn)}";

            Logger.Log("cmd.exe " + rifeNcnn.StartInfo.Arguments, true);

            rifeNcnn.OutputDataReceived += (sender, outLine) => { LogOutput("[O] " + outLine.Data, Implementations.rifeNcnn); };
            rifeNcnn.ErrorDataReceived += (sender, outLine) => { LogOutput("[E] " + outLine.Data, Implementations.rifeNcnn, true); };
            rifeNcnn.Start();
            rifeNcnn.BeginOutputReadLine();
            rifeNcnn.BeginErrorReadLine();

            while (!rifeNcnn.HasExited) await Task.Delay(1);
        }

        public static async Task RunRifeNcnnVs(string framesPath, string outPath, float factor, string mdl, bool rt = false)
        {
            if (Interpolate.canceled) return;

            AiInfo ai = Implementations.rifeNcnnVs;
            processTimeMulti.Restart();

            try
            {
                Logger.Log($"Executando RIFE (NCNN-VS){(InterpolateUtils.UseUhd(Interpolate.currentSettings.OutputResolution) ? " (modo UHD)" : "")}...", false);
                Logger.Log("Dispositivo: " + ProcessingDevice.DescribeNcnn());
                await RunRifeNcnnVsProcess(framesPath, factor, outPath, mdl, Interpolate.currentSettings.OutputResolution, rt);
            }
            catch (Exception e)
            {
                Logger.Log($"Erro ao executar {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        static async Task RunRifeNcnnVsProcess(string inPath, float factor, string outPath, string mdl, Size res, bool rt = false)
        {
            IoUtils.CreateDir(outPath);
            Process rifeNcnnVs = OsUtils.NewProcess(true);
            string avDir = Path.Combine(Paths.GetPkgPath(), Paths.audioVideoDir);
            string pkgDir = Path.Combine(Paths.GetPkgPath(), Implementations.rifeNcnnVs.PkgDir);
            int gpuId = NcnnGpuIds.Split(',')[0].GetInt();

            var vsSettings = new VapourSynthUtils.VsSettings()
            {
                InterpSettings = Interpolate.currentSettings,
                Alpha = Interpolate.currentSettings.alpha,
                ModelDir = mdl,
                Factor = factor,
                Res = res,
                Uhd = InterpolateUtils.UseUhd(res),
                GpuId = gpuId,
                GpuThreads = NcnnUtils.GetRifeNcnnGpuThreads(res, gpuId, Implementations.rifeNcnnVs),
                SceneDetectSensitivity = Config.GetBool(Config.Key.scnDetect) ? Config.GetFloat(Config.Key.scnDetectValue) * 0.7f : 0f,
                Loop = Config.GetBool(Config.Key.enableLoop),
                MatchDuration = Config.GetBool(Config.Key.fixOutputDuration),
                Dedupe = Interpolate.currentSettings.dedupe,
                Realtime = rt,
                Osd = Config.GetBool(Config.Key.vsRtShowOsd),
            };

            if (rt)
            {
                Logger.ClearLogBox();
                Logger.Log($"Iniciando. Espaço pausa, setas esquerda/direita avançam e retrocedem (pode ser lento), Q sai.");
                AiStartedRt(rifeNcnnVs, inPath);
            }
            else
            {
                SetProgressCheck(Interpolate.currentMediaFile.FrameCount, factor, Implementations.rifeNcnnVs.LogFilename);
                AiStarted(rifeNcnnVs, 1000, inPath);
            }

            IoUtils.TryDeleteIfExists(Path.Combine(Interpolate.currentSettings.tempFolder, "alpha.mkv"));
            string vspipe = $"vspipe rife.py {VapourSynthUtils.GetVsPipeArgs(vsSettings)}";
            string ffmpeg = $"{Path.Combine(avDir, "ffmpeg").Wrap()} -loglevel warning -stats -y";
            string baseArgs = $"/C cd /D {pkgDir.Wrap()}";

            if (vsSettings.Alpha)
            {
                rifeNcnnVs.StartInfo.Arguments = $"{baseArgs} && {vspipe} --arg alpha=\"True\" -c y4m - | {ffmpeg} {await Export.GetPipedFfmpegCmd(alpha: Export.AlphaMode.AlphaOut)} && {vspipe} -c y4m - | {ffmpeg} {await Export.GetPipedFfmpegCmd(alpha: Export.AlphaMode.AlphaIn)}";
            }
            else
            {
                rifeNcnnVs.StartInfo.Arguments = $"{baseArgs} && {vspipe} -c y4m - | {ffmpeg} {await Export.GetPipedFfmpegCmd(rt)}";
            }

            Logger.Log($"cmd.exe {rifeNcnnVs.StartInfo.Arguments}", true, toConsole: Cli.Verbose);
            rifeNcnnVs.OutputDataReceived += (sender, outLine) => { LogOutput($"[O] {outLine.Data}", Implementations.rifeNcnnVs); };
            rifeNcnnVs.ErrorDataReceived += (sender, outLine) => { LogOutput($"[E] {outLine.Data}".Replace("] Information: ", "] "), Implementations.rifeNcnnVs, true); };
            rifeNcnnVs.Start();
            rifeNcnnVs.BeginOutputReadLine();
            rifeNcnnVs.BeginErrorReadLine();
            while (!rifeNcnnVs.HasExited) await Task.Delay(1);
        }

        public static async Task RunDainNcnn(string framesPath, string outPath, float factor, string mdl, int tilesize)
        {
            AiInfo ai = Implementations.dainNcnn;

            if (Interpolate.currentlyUsingAutoEnc)      // Ensure AutoEnc is not paused
                AutoEncode.paused = false;

            try
            {
                await RunDainNcnnProcess(framesPath, outPath, factor, mdl, tilesize);
                await NcnnUtils.DeleteNcnnDupes(outPath, factor);
            }
            catch (Exception e)
            {
                Logger.Log($"Erro ao executar {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        public static async Task RunDainNcnnProcess(string framesPath, string outPath, float factor, string mdl, int tilesize)
        {
            string dainDir = Path.Combine(Paths.GetPkgPath(), Implementations.dainNcnn.PkgDir);
            Directory.CreateDirectory(outPath);
            Process dain = OsUtils.NewProcess(true);
            AiStarted(dain, 1500);
            SetProgressCheck(outPath, factor);
            int targetFrames = ((IoUtils.GetAmountOfFiles(lastInPath, false, "*.*") * factor).RoundToInt());

            string args = $" -v -i {framesPath.Wrap()} -o {outPath.Wrap()} -n {targetFrames} -m {mdl.Lower()}" +
                $" -t {NcnnUtils.GetNcnnTilesize(tilesize)} -g {NcnnGpuIds} -f {NcnnUtils.GetNcnnPattern()} -j 2:1:2";

            dain.StartInfo.Arguments = $"/C cd /D {dainDir.Wrap()} & dain-ncnn-vulkan.exe {args}";
            Logger.Log("Executando DAIN...", false);
                Logger.Log("Dispositivo: " + ProcessingDevice.DescribeNcnn());
            Logger.Log("cmd.exe " + dain.StartInfo.Arguments, true);

            dain.OutputDataReceived += (sender, outLine) => { LogOutput("[O] " + outLine.Data, Implementations.dainNcnn); };
            dain.ErrorDataReceived += (sender, outLine) => { LogOutput("[E] " + outLine.Data, Implementations.dainNcnn, true); };
            dain.Start();
            dain.BeginOutputReadLine();
            dain.BeginErrorReadLine();

            while (!dain.HasExited)
                await Task.Delay(100);
        }

        public static async Task RunXvfiCuda(string framesPath, float interpFactor, string mdl)
        {
            AiInfo ai = Implementations.xvfiCuda;

            if (Interpolate.currentlyUsingAutoEnc)      // Ensure AutoEnc is not paused
                AutoEncode.paused = false;

            try
            {
                string xvfiDir = Path.Combine(Paths.GetPkgPath(), Implementations.xvfiCuda.PkgDir);
                string script = "main.py";

                if (!File.Exists(Path.Combine(xvfiDir, script)))
                {
                    Interpolate.Cancel("Script do XVFI não encontrado! Verifique se você não modificou nenhum arquivo.");
                    return;
                }

                await RunXvfiCudaProcess(framesPath, Paths.interpDir, script, interpFactor, mdl);
            }
            catch (Exception e)
            {
                Logger.Log($"Erro ao executar {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        public static async Task RunXvfiCudaProcess(string inPath, string outDir, string script, float interpFactor, string mdlDir)
        {
            string pkgPath = Path.Combine(Paths.GetPkgPath(), Implementations.xvfiCuda.PkgDir);
            string basePath = inPath.GetParentDir();
            string outPath = Path.Combine(basePath, outDir);
            Directory.CreateDirectory(outPath);
            string mdlArgs = File.ReadAllText(Path.Combine(pkgPath, mdlDir, "args.ini"));
            string args = $" --custom_path {basePath.Wrap()} --input {inPath.Wrap()} --output {outPath.Wrap()} --mdl_dir {mdlDir}" +
                $" --multiple {interpFactor} --gpu 0 {mdlArgs}";

            Process xvfiPy = OsUtils.NewProcess(true);
            AiStarted(xvfiPy, 3500);
            SetProgressCheck(Path.Combine(Interpolate.currentSettings.tempFolder, outDir), interpFactor);
            xvfiPy.StartInfo.Arguments = $"/C cd /D {pkgPath.Wrap()} & " +
                $"set CUDA_VISIBLE_DEVICES={Config.Get(Config.Key.torchGpus)} & {Python.GetPyCmd()} {script} {args}";
            Logger.Log($"Executando XVFI (CUDA)...", false);
            Logger.Log("Dispositivo: " + ProcessingDevice.DescribeCuda());
            Logger.Log("cmd.exe " + xvfiPy.StartInfo.Arguments, true);

            xvfiPy.OutputDataReceived += (sender, outLine) => { LogOutput(outLine.Data, Implementations.xvfiCuda); };
            xvfiPy.ErrorDataReceived += (sender, outLine) => { LogOutput("[E] " + outLine.Data, Implementations.xvfiCuda, true); };
            xvfiPy.Start();
            xvfiPy.BeginOutputReadLine();
            xvfiPy.BeginErrorReadLine();

            while (!xvfiPy.HasExited) await Task.Delay(1);
        }

        public static async Task RunIfrnetNcnn(string framesPath, string outPath, float factor, string mdl)
        {
            AiInfo ai = Implementations.ifrnetNcnn;

            processTimeMulti.Restart();

            try
            {
                Logger.Log($"Executando IFRNet (NCNN){(InterpolateUtils.UseUhd() ? " (modo UHD)" : "")}...", false);
                Logger.Log("Dispositivo: " + ProcessingDevice.DescribeNcnn());

                await RunIfrnetNcnnProcess(framesPath, factor, outPath, mdl);
                await NcnnUtils.DeleteNcnnDupes(outPath, factor);
            }
            catch (Exception e)
            {
                Logger.Log($"Erro ao executar {ai.FriendlyName}: {e.Message}");
                Logger.Log("Stack Trace: " + e.StackTrace, true);
            }

            await AiFinished(ai.NameShort);
        }

        static async Task RunIfrnetNcnnProcess(string inPath, float factor, string outPath, string mdl)
        {
            Directory.CreateDirectory(outPath);
            Process ifrnetNcnn = OsUtils.NewProcess(true);
            AiStarted(ifrnetNcnn, 1500, inPath);
            SetProgressCheck(outPath, factor);
            //int targetFrames = ((IoUtils.GetAmountOfFiles(lastInPath, false, "*.*") * factor).RoundToInt()); // TODO: Maybe won't work with fractional factors ??
            //string frames = mdl.Contains("v4") ? $"-n {targetFrames}" : "";
            string uhdStr = ""; // InterpolateUtils.UseUhd() ? "-u" : "";
            string ttaStr = ""; // Config.GetBool(Config.Key.rifeNcnnUseTta, false) ? "-x" : "";

            ifrnetNcnn.StartInfo.Arguments = $"/C cd /D {Path.Combine(Paths.GetPkgPath(), Implementations.ifrnetNcnn.PkgDir).Wrap()} & ifrnet-ncnn-vulkan.exe " +
                $" -v -i {inPath.Wrap()} -o {outPath.Wrap()} -m {mdl} {ttaStr} {uhdStr} -g {NcnnGpuIds} -f {NcnnUtils.GetNcnnPattern()} -j {NcnnUtils.GetNcnnThreads(Implementations.ifrnetNcnn)}";

            Logger.Log("cmd.exe " + ifrnetNcnn.StartInfo.Arguments, true);

            ifrnetNcnn.OutputDataReceived += (sender, outLine) => { LogOutput("[O] " + outLine.Data, Implementations.ifrnetNcnn); };
            ifrnetNcnn.ErrorDataReceived += (sender, outLine) => { LogOutput("[E] " + outLine.Data, Implementations.ifrnetNcnn, true); };
            ifrnetNcnn.Start();
            ifrnetNcnn.BeginOutputReadLine();
            ifrnetNcnn.BeginErrorReadLine();

            while (!ifrnetNcnn.HasExited) await Task.Delay(1);
        }

        private static readonly Regex FfmpegLogMemAddr = new Regex(@" @\s*(?:0x)?(?<addr>[0-9A-Fa-f]{8,16})(?=\])", RegexOptions.Compiled);

        private static string GetLastLogLines(string log = "", int lineCount = 6, bool beautify = true)
        {
            if (log.IsEmpty())
            {
                if (logName.IsNotEmpty())
                    log = logName;
                else
                    return "N/A";
            }

            var lll = Logger.GetSessionLogLastLines(log, lineCount);

            if (!beautify)
                return string.Join("\n", lll);

            var beautified = lll.Select(l => $"[{string.Join(" ", l.Split(' ').Skip(2))}".Replace("]: [E]", "]").Replace("]: [O]", "]"));
            beautified = beautified.Select(l => FfmpegLogMemAddr.Replace(l, "")); // Remove memory addresses from ffmpeg outputs
            beautified = beautified.Where(l => !l.Contains("\\vapoursynth.pyx\", line ")); // Remove useless VS traceback lines
            return string.Join("\n", beautified);
        }

        private static string GetPyErrorDesc (string line)
        {
            // Module not found or failed to import
            if (line.Contains("ImportError") || line.Contains("ModuleNotFoundError"))
                return $"Python failed to find or import a module!";

            // Unicode error
            if (line.Contains("UnicodeEncodeError"))
                return $"O caminho do arquivo parece ter caracteres inválidos - remova-os e tente de novo!";

            // Generic errors
            if (line.Contains("RuntimeError") || line.Contains("AttributeError") || line.Contains("OSError"))
                return $"A python error occured during interpolation!";

            return "";
        }

        /// <summary> Rodape das mensagens de erro: dispositivo usado e causa provavel (se reconhecida). </summary>
        private static string ErrorFooter(AiInfo ai, string extraText)
        {
            try
            {
                string device = (ai != null && ai.Backend == AiInfo.AiBackend.Ncnn) ? ProcessingDevice.DescribeNcnn() : ProcessingDevice.DescribeCuda();
                string text = (extraText ?? "") + "\n" + GetLastLogLines(logName);
                string explain = ProcessingDevice.ExplainError(text);
                return $"\n\n--------------------\nDispositivo de processamento: {device}" + (explain.IsEmpty() ? "" : "\n\n" + explain);
            }
            catch
            {
                return "";
            }
        }

        private static void LogOutput(string line, AiInfo ai, bool err = false)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 6)
                return;

            logName = ai.LogFilename;
            Logger.Log(line, true, false, ai.LogFilename, toConsole: Cli.Verbose);
            void ShowErrorBox(string msg) => UiUtils.ShowMessageBox(msg + ErrorFooter(ai, line), UiUtils.MessageType.Error, monospace: true);
            string lineLow = line.Lower();

            if (ai.Backend == AiInfo.AiBackend.Pytorch) // Pytorch specific
            {
                if (line.Contains("ff:nocuda-cpu"))
                    Logger.Log("AVISO: nenhuma GPU compatível com CUDA disponível, executando na CPU!");

                if (!hasShownError && err && line.Contains("ModuleNotFoundError"))
                {
                    hasShownError = true;
                    ShowErrorBox($"Falta um módulo do Python.\nVeja os detalhes em {ai.LogFilename}.\n\n{line}");
                }

                if (!hasShownError && lineLow.Contains("no longer supports this gpu"))
                {
                    hasShownError = true;
                    ShowErrorBox($"Sua GPU parece ser antiga e não é suportada por esta versão do PyTorch!\n\n{line}");
                }

                if (!hasShownError && lineLow.Contains("error(s) in loading state_dict"))
                {
                    hasShownError = true;
                    string msg = (Interpolate.currentSettings.ai.NameInternal == Implementations.flavrCuda.NameInternal) ? "\n\nNo FLAVR, é preciso escolher o modelo correto para cada escala!" : "";
                    ShowErrorBox($"Erro ao carregar o modelo de IA!\n\n{line}{msg}");
                }

                if (!hasShownError && line.Contains("UnicodeEncodeError"))
                {
                    hasShownError = true;
                    ShowErrorBox($"O caminho do arquivo parece ter caracteres inválidos - remova-os e tente de novo!\n\n{line}");
                }

                if (!hasShownError && err && (line.Contains("RuntimeError") || line.Contains("ImportError") || line.Contains("OSError")))
                {
                    hasShownError = true;
                    ShowErrorBox($"Ocorreu um erro do Python durante a interpolação!\nVeja o log para detalhes:\n\n{GetLastLogLines(logName)}");
                }
            }

            if (ai.Backend == AiInfo.AiBackend.Ncnn) // NCNN specific
            {
                if (!hasShownError && err && line.Contains("vkCreateInstance failed"))
                {
                    hasShownError = true;
                    ShowErrorBox($"Falha ao iniciar o Vulkan!\n\n{line}\n\nProvavelmente sua GPU não é compatível ou o driver está desatualizado. Escolha a CPU (ID -1) em Configurações para continuar.");
                }

                if (!hasShownError && err && line.Contains("vkAllocateMemory failed"))
                {
                    hasShownError = true;
                    bool usingDain = (Interpolate.currentSettings.ai.NameInternal == Implementations.dainNcnn.NameInternal);
                    string msg = usingDain ? "\n\nTente reduzir o tamanho do bloco (tile) nas configurações da IA." : "\n\nTente uma resolução menor (Configurações > Tamanho máximo do vídeo).";
                    ShowErrorBox($"A memória de vídeo (VRAM) acabou!\n\n{line}{msg}");
                }

                if (!hasShownError && err && line.Contains("invalid gpu device"))
                {
                    hasShownError = true;
                    ShowErrorBox($"Ocorreu um erro do Vulkan durante a interpolação!\n\n{line}\n\nOs IDs de GPU estão corretos? (Configurações > Dispositivo NCNN)");
                }

                if (!hasShownError && err && line.Contains(" failed") && line.Contains("vk"))
                {
                    hasShownError = true;
                    ShowErrorBox($"Ocorreu um erro do Vulkan durante a interpolação!\n\n{GetLastLogLines(logName)}");
                }
            }

            if (ai.Piped) // VS specific
            {
                // Check for python errors
                var pyErr = GetPyErrorDesc(line);

                if(!hasShownError && pyErr.IsNotEmpty())
                {
                    hasShownError = true;
                    ShowErrorBox($"{pyErr}\n\n{GetLastLogLines()}");
                }

                if (!hasShownError && Interpolate.currentSettings.outSettings.Format != Enums.Output.Format.Realtime && (line.Contains("Task finished with error code") || line.Contains("fwrite() call failed")))
                {
                    hasShownError = true;
                    ShowErrorBox($"A interpolação via VapourSynth falhou com um erro desconhecido. Veja o log:\n\n{GetLastLogLines(logName)}");
                }

                if (!hasShownError && lineLow.Contains("allocate memory failed"))
                {
                    hasShownError = true;
                    ShowErrorBox($"Memória RAM insuficiente!\nFeche alguns programas para liberar memória.\n\n{line}");
                }

                if (!hasShownError && line.Contains("vapoursynth.Error:"))
                {
                    hasShownError = true;
                    ShowErrorBox($"Erro do VapourSynth:\n\n{GetLastLogLines()}");
                }
            }

            if (!hasShownError && err && lineLow.Contains("out of memory"))
            {
                hasShownError = true;
                ShowErrorBox($"A memória de vídeo (VRAM) acabou! Use um vídeo de resolução menor, a opção Tamanho máximo do vídeo nas configurações, ou processe na CPU.\n\n{line}");
            }

            if (!hasShownError && lineLow.Contains("illegal memory access"))
            {
                hasShownError = true;
                ShowErrorBox($"Sua GPU parece instável! Se houver overclock ativado, desative-o!\n\n{line}");
            }

            if (hasShownError)
                Interpolate.Cancel();

            InterpolationProgress.UpdateLastFrameFromInterpOutput(line);
        }


    }
}
