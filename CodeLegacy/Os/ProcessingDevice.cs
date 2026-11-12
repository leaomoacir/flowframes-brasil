using Flowframes.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;

namespace Flowframes.Os
{
    /// <summary>
    /// Descobre e descreve o dispositivo de processamento (GPU Vulkan, GPU CUDA ou CPU),
    /// corrige configurações inválidas e traduz erros técnicos das IAs em causas prováveis.
    /// </summary>
    public static class ProcessingDevice
    {
        private static string _cpuName = null;
        private static bool _warnedFallback = false;

        public static int CpuThreads => Environment.ProcessorCount;

        public static string CpuName()
        {
            if (_cpuName != null)
                return _cpuName;

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        _cpuName = Convert.ToString(mo["Name"]).Trim();
                        break;
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(_cpuName))
                _cpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "CPU desconhecida";

            _cpuName = System.Text.RegularExpressions.Regex.Replace(_cpuName, @"\s+", " ");
            return _cpuName;
        }

        private static List<VulkanUtils.VkDevice> VkList => VulkanUtils.VkDevices ?? new List<VulkanUtils.VkDevice>();

        /// <summary> IDs de dispositivo NCNN que serão realmente usados ("-1" = CPU). Corrige IDs inválidos automaticamente. </summary>
        public static string EffectiveNcnnIds()
        {
            string raw = Config.Get(Config.Key.ncnnGpus).Trim();
            var ids = new List<int>();

            foreach (string part in raw.Split(','))
            {
                if (int.TryParse(part.Trim(), out int id))
                    ids.Add(id);
            }

            // CPU escolhida (ou nenhuma GPU Vulkan existe): usa somente a CPU
            if (ids.Contains(-1) || VkList.Count == 0)
            {
                if (!ids.Contains(-1) && !_warnedFallback)
                {
                    _warnedFallback = true;
                    Logger.Log("Nenhuma GPU Vulkan disponível: o processamento NCNN será feito na CPU.");
                }

                return "-1";
            }

            var valid = ids.Where(i => VkList.Any(d => d.Id == i)).Distinct().ToList();

            if (valid.Count == 0)
            {
                int best = VkList.OrderByDescending(d => d.ComputeQueueCount).First().Id;

                if (!_warnedFallback)
                {
                    _warnedFallback = true;
                    Logger.Log($"O ID de GPU configurado ('{raw}') não existe neste computador. Usando a GPU [{best}] automaticamente.");
                }

                return best.ToString();
            }

            return string.Join(",", valid);
        }

        public static bool NcnnUsesCpu(string ids = null)
        {
            return (ids ?? EffectiveNcnnIds()).Split(',')[0].Trim().StartsWith("-");
        }

        /// <summary> Texto completo, para o log. </summary>
        public static string DescribeNcnn(string ids = null)
        {
            ids = ids ?? EffectiveNcnnIds();

            if (NcnnUsesCpu(ids))
                return $"CPU - {CpuName()} ({CpuThreads} threads). Mais lento que uma GPU.";

            var names = ids.Split(',').Select(s => int.Parse(s.Trim())).Select(id =>
            {
                var d = VkList.FirstOrDefault(x => x.Id == id);
                return d == null ? $"[{id}]" : $"[{id}] {d.Name}{TypeSuffix(d)}";
            });

            return "GPU " + string.Join(" + ", names) + " (Vulkan)";
        }

        /// <summary> Texto curto, para a tela de configurações. </summary>
        public static string DescribeNcnnShort(string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return "";

            var parts = ids.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

            if (parts.Any(p => p.StartsWith("-")))
                return "Usará a CPU (mais lento)";

            if (!parts.All(p => int.TryParse(p, out _)))
                return "ID inválido";

            var names = parts.Select(p =>
            {
                var d = VkList.FirstOrDefault(x => x.Id == int.Parse(p));
                return d == null ? $"[{p}] não existe!" : $"[{p}] {ShortName(d.Name)}";
            });

            return "Usará: " + string.Join(", ", names);
        }

        public static string DescribeCuda()
        {
            if (NvApi.NvGpus != null && NvApi.NvGpus.Any())
            {
                int idx = Math.Max(0, Math.Min(NvApi.NvGpus.Count - 1, Config.Get(Config.Key.torchGpus).Trim().Split(',')[0].Trim().GetInt()));
                var gpu = NvApi.NvGpus[idx];
                return $"GPU Nvidia [{idx}] {gpu.FullName} ({gpu.GetVramGb().ToString("0.#")} GB) via CUDA";
            }

            return "CPU (nenhuma GPU Nvidia/CUDA encontrada). PyTorch na CPU é muito mais lento - prefira uma versão NCNN.";
        }

        public static string DescribeTorchShort(string ids)
        {
            if (NvApi.NvGpus == null || !NvApi.NvGpus.Any())
                return "Sem GPU Nvidia: usará a CPU";

            if (!int.TryParse((ids ?? "").Split(',')[0].Trim(), out int idx) || idx < 0 || idx >= NvApi.NvGpus.Count)
                return "ID inválido";

            return "Usará: " + ShortName(NvApi.NvGpus[idx].FullName);
        }

        /// <summary> Resumo do hardware para o log inicial. </summary>
        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hardware de processamento:");
            sb.AppendLine($"  CPU: {CpuName()} ({CpuThreads} threads)");

            if (VkList.Count > 0)
                sb.AppendLine("  GPUs Vulkan (NCNN): " + string.Join(" | ", VkList.Select(d => $"[{d.Id}] {d.Name}{TypeSuffix(d)}")));
            else
                sb.AppendLine("  GPUs Vulkan (NCNN): nenhuma encontrada");

            if (NvApi.NvGpus != null && NvApi.NvGpus.Any())
                sb.AppendLine("  GPUs Nvidia (CUDA): " + string.Join(" | ", NvApi.NvGpus.Select((g, i) => $"[{i}] {g.FullName} ({g.GetVramGb().ToString("0.#")} GB)")));
            else
                sb.AppendLine("  GPUs Nvidia (CUDA): nenhuma encontrada");

            sb.Append("  Em uso nas versões NCNN: " + DescribeNcnn());
            return sb.ToString();
        }

        private static string TypeSuffix(VulkanUtils.VkDevice d)
        {
            switch ((d.Type ?? "").ToLowerInvariant())
            {
                case "discretegpu": return " (dedicada)";
                case "integratedgpu": return " (integrada)";
                case "cpu": return " (software/CPU)";
                case "virtualgpu": return " (virtual)";
                default: return "";
            }
        }

        private static string ShortName(string name)
        {
            return (name ?? "").Replace("NVIDIA ", "").Replace("GeForce ", "").Replace("(TM)", "").Replace("(R)", "").Trim();
        }

        /// <summary>
        /// Procura no texto do log sinais conhecidos de falha e devolve uma explicação com a causa provável e o que fazer.
        /// Retorna "" se nada for reconhecido.
        /// </summary>
        public static string ExplainError(string logText)
        {
            if (string.IsNullOrWhiteSpace(logText))
                return "";

            string t = logText.ToLowerInvariant();
            var causes = new List<string>();

            if (t.Contains("vkallocatememory failed") || t.Contains("out of device memory") || t.Contains("cuda out of memory") || t.Contains("out_of_device_memory") || t.Contains("failed -2"))
                causes.Add("MEMÓRIA DE VÍDEO (VRAM) INSUFICIENTE.\n" +
                           "  - Reduza o \"Tamanho máximo do vídeo de entrada (altura)\" nas Configurações rápidas.\n" +
                           "  - Diminua as \"Threads de processamento da GPU (NCNN)\" ou desative o modo UHD.\n" +
                           "  - Feche jogos/navegadores que usam a GPU, ou selecione a CPU (ID -1) em Configurações.");

            if (t.Contains("vkwaitforfences failed") || t.Contains("device lost") || t.Contains("device_lost") || t.Contains("failed -4") || t.Contains("vkqueuesubmit failed"))
                causes.Add("A GPU PAROU DE RESPONDER (driver reiniciado ou tempo esgotado).\n" +
                           "  - Atualize o driver da placa de vídeo.\n" +
                           "  - Reduza a resolução do vídeo e as threads da GPU; se continuar, use a CPU (ID -1).");

            if (t.Contains("vkcreateinstance failed") || t.Contains("invalid gpu device") || t.Contains("no vulkan") || t.Contains("create_gpu_instance") || t.Contains("vulkan is not") || t.Contains("vkenumeratephysicaldevices"))
                causes.Add("VULKAN INDISPONÍVEL OU ID DE GPU INVÁLIDO.\n" +
                           "  - Atualize o driver da placa de vídeo (Nvidia/AMD/Intel).\n" +
                           "  - Em Configurações > \"Dispositivo (NCNN)\", escolha uma GPU válida ou -1 para usar a CPU.");

            if (t.Contains("failed to open") || t.Contains("load_param") || t.Contains("load_model") || t.Contains("flownet.param") || t.Contains("param file") || t.Contains("model file"))
                causes.Add("ARQUIVOS DO MODELO DE IA AUSENTES OU CORROMPIDOS.\n" +
                           "  - Em Configurações > \"Abrir baixador de modelos\", baixe o modelo novamente.\n" +
                           "  - Se persistir, use \"Limpar cache de modelos\" e baixe de novo.");

            if (t.Contains("bad_alloc") || t.Contains("bad allocation") || t.Contains("out of memory") && !causes.Any())
                causes.Add("MEMÓRIA RAM INSUFICIENTE.\n" +
                           "  - Feche outros programas e/ou reduza a resolução máxima do vídeo.");

            if (t.Contains("modulenotfounderror") || t.Contains("no module named"))
                causes.Add("FALTA UM MÓDULO DO PYTHON (versões CUDA/PyTorch).\n" +
                           "  - Instale as dependências indicadas em PythonDependencies.md ou use uma versão NCNN (não precisa de Python).");

            if (t.Contains("no longer supports this gpu") || t.Contains("no kernel image"))
                causes.Add("SUA GPU NÃO É COMPATÍVEL COM ESTA VERSÃO DO PYTORCH/CUDA.\n" +
                           "  - Use as versões NCNN (RIFE NCNN), que funcionam em qualquer GPU com Vulkan ou na CPU.");

            if (t.Contains("access violation") || t.Contains("0xc0000005") || t.Contains("segmentation fault"))
                causes.Add("O PROCESSO DA IA FECHOU COM ERRO DE MEMÓRIA (violação de acesso).\n" +
                           "  - Normalmente é driver de vídeo desatualizado ou antivírus/anti-cheat interferindo. Atualize o driver e tente de novo.");

            if (!causes.Any())
                return "";

            return "Causa provável:\n" + string.Join("\n", causes);
        }
    }
}
