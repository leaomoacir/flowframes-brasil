using Flowframes.Forms.Main;
using Flowframes.MiscUtils;
using System;
using System.Text.RegularExpressions;
using static Flowframes.AvProcess;

namespace Flowframes.Media
{
    class AvOutputHandler
    {
        public static readonly string prefix = "[ffmpeg]";

        public static void LogOutput(string line, ref string appendStr, string logFilename, LogMode logMode, bool showProgressBar)
        {
            if (Interpolate.canceled || string.IsNullOrWhiteSpace(line) || line.Trim().Length < 1)
                return;

            bool hidden = logMode == LogMode.Hidden;

            if (HideMessage(line)) // Don't print certain warnings 
                hidden = true;

            bool replaceLastLine = logMode == LogMode.OnlyLastLine;

            if (line.Contains("time=") && (line.StartsWith("frame=") || line.StartsWith("size=")))
                line = FormatUtils.BeautifyFfmpegStats(line);

            appendStr += Environment.NewLine + line;
            Logger.Log($"{prefix} {line}", hidden, replaceLastLine, logFilename, toConsole: Cli.Verbose || logMode != LogMode.Hidden);

            if (!hidden && showProgressBar && line.Contains("Time:"))
            {
                Regex timeRegex = new Regex("(?<=Time:).*(?= )");
                UpdateFfmpegProgress(timeRegex.Match(line).Value);
            }


            if (line.Contains("Unable to"))
            {
                Interpolate.Cancel($"Erro: {line}");
                return;
            }

            if (line.Contains("Could not open file"))
            {
                Interpolate.Cancel($"Erro: {line}");
                return;
            }

            if (line.Contains("No NVENC capable devices found") || line.MatchesWildcard("*nvcuda.dll*"))
            {
                Interpolate.Cancel($"Erro: {line}\n\nVerifique se você tem uma GPU Nvidia compatível com NVENC.");
                return;
            }

            if (line.Contains("not currently supported in container") || line.Contains("Unsupported codec id"))
            {
                Interpolate.Cancel($"Erro: {line}\n\nParece que você está tentando copiar um fluxo para um contêiner que não suporta este codec.");
                return;
            }

            if (line.Contains("Subtitle encoding currently only possible from text to text or bitmap to bitmap"))
            {
                Interpolate.Cancel($"Erro: {line}\n\nNão é possível converter legendas em imagem para legendas em texto. Use a opção de copiar legendas com um contêiner compatível.");
                return;
            }

            if (line.Contains("Only VP8 or VP9 or AV1 video and Vorbis or Opus audio and WebVTT subtitles are supported for WebM"))
            {
                Interpolate.Cancel($"Erro: {line}\n\nParece que você está tentando copiar um fluxo não suportado para WEBM!");
                return;
            }

            if (line.MatchesWildcard("*codec*not supported*"))
            {
                Interpolate.Cancel($"Erro: {line}\n\nTente usar outro codec ou formato de contêiner.");
                return;
            }

            if (line.Contains("GIF muxer supports only a single video GIF stream"))
            {
                Interpolate.Cancel($"Erro: {line}\n\nVocê tentou incluir um fluxo que não é GIF em um arquivo GIF.");
                return;
            }

            if (line.Contains("Width and height of input videos must be same"))
            {
                Interpolate.Cancel($"Erro: {line}");
                return;
            }
        }

        public static void UpdateFfmpegProgress(string ffmpegTime)
        {
            try
            {
                Form1 form = Program.mainForm;
                long currInDuration = (form.currInDurationCut < form.currInDuration) ? form.currInDurationCut : form.currInDuration;

                if (currInDuration < 1)
                {
                    Program.mainForm.SetProgress(0);
                    return;
                }

                long total = currInDuration / 100;
                long current = FormatUtils.TimestampToMs(ffmpegTime);
                int progress = Convert.ToInt32(current / total);
                Program.mainForm.SetProgress(progress);
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to get ffmpeg progress: {e.Message}", true);
            }
        }

        static bool HideMessage(string msg)
        {
            string[] hiddenMsgs = new string[] { "can produce invalid output", "pixel format", "provided invalid", "Non-monotonous", "not enough frames to estimate rate", "invalid dropping", "message repeated" };

            foreach (string str in hiddenMsgs)
                if (msg.MatchesWildcard($"*{str}*"))
                    return true;

            return false;
        }
    }
}
