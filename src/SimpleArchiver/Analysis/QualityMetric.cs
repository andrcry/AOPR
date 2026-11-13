using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SimpleArchiver
{
    // Клас TeamConfig перенесено в Common/TeamConfig.cs: він читає справжній team_config.json від викладача

    // Результат вимірювання метрики Q
    public class QEvaluation
    {
        public string Name { get; set; } = "";
        public long OriginalBytes { get; set; }
        public long ArchiveBytes { get; set; }
        public double ElapsedSeconds { get; set; }
        public bool RestoredExact { get; set; }
        public double S { get; set; }
        public double T { get; set; }
        public double Q { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public static class QualityMetric
    {
        const string EvalPassword = "sarc_eval_password_2026";

        // Пункти 3 та 8: Обчислення Q = alpha * S + beta * T та перевірка побайтового відновлення без винятків
        public static QEvaluation Evaluate(
            List<ArchiveFile> corpus,
            Settings settings,
            TeamConfig config,
            string configurationName = "Базова система",
            string? logFilePath = null
        )
        {
            var result = new QEvaluation { Name = configurationName };
            if (corpus == null || corpus.Count == 0)
                return result;

            long bOrig = 0;
            foreach (var f in corpus)
                bOrig += f.Data.Length;
            result.OriginalBytes = bOrig;

            try
            {
                var sw = Stopwatch.StartNew();
                List<byte[]> volumes = ArchiveWriter.CreateArchive(corpus, EvalPassword, settings);
                List<ArchiveFile> restored = ArchiveReader.ReadArchive(volumes, EvalPassword);
                sw.Stop();

                long bArc = 0;
                foreach (var v in volumes)
                    bArc += v.Length;

                result.ArchiveBytes = bArc;
                result.ElapsedSeconds = Math.Max(sw.Elapsed.TotalSeconds, 0.0001);
                result.RestoredExact = CheckByteForByte(corpus, restored);

                result.S = bOrig > 0 ? (double)bOrig / (bOrig + bArc) : 0.0;
                result.T = config.TRef / (config.TRef + result.ElapsedSeconds);

                // Якщо хоча б один файл корпусу не відновлено побайтово, Q = 0
                result.Q = result.RestoredExact
                    ? (config.Alpha * result.S + config.Beta * result.T)
                    : 0.0;

                if (logFilePath != null)
                {
                    File.AppendAllText(
                        logFilePath,
                        $"[{DateTime.UtcNow:O}] [OK] Mode='{configurationName}' Files={corpus.Count} "
                            + $"B_orig={bOrig} B_arc={bArc} Time={result.ElapsedSeconds:F4}s Q={result.Q:F5} Exact={result.RestoredExact}\n"
                    );
                }
            }
            catch (Exception ex)
            {
                result.RestoredExact = false;
                result.Q = 0.0;
                result.ErrorMessage = ex.Message;

                if (logFilePath != null)
                {
                    File.AppendAllText(
                        logFilePath,
                        $"[{DateTime.UtcNow:O}] [HANDLED_ERROR] Mode='{configurationName}' Error='{ex.Message}'\n"
                    );
                }
            }

            return result;
        }

        static bool CheckByteForByte(List<ArchiveFile> expected, List<ArchiveFile> actual)
        {
            if (expected.Count != actual.Count)
                return false;

            var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var f in expected)
                map[f.Name] = f.Data;

            foreach (var restored in actual)
            {
                if (!map.TryGetValue(restored.Name, out byte[]? orig))
                    return false;
                if (orig.Length != restored.Data.Length)
                    return false;
                for (int i = 0; i < orig.Length; i++)
                {
                    if (orig[i] != restored.Data[i])
                        return false;
                }
                if (Crc32.Compute(orig) != Crc32.Compute(restored.Data))
                    return false;
            }
            return true;
        }

        // Пункт 10: Таблиця впливу кожного блока на Q (Ablation Study)
        public static string BuildAblationTable(
            List<ArchiveFile> corpus,
            TeamConfig config,
            string logPath = "experiments/corpus_run.log"
        )
        {
            string? dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(logPath))
                File.Delete(logPath);

            var configs = new List<(string Label, Action<Settings> Apply)>
            {
                (
                    "Повна система (Lazy LZ77 + ДП-блоки + Прогноз)",
                    s =>
                    {
                        s.Parser = ParserKind.Lazy;
                        s.UseDpBlockSplit = true;
                        s.UsePrediction = true;
                    }
                ),
                (
                    "Без прогнозу байта (--predict вимкнено)",
                    s =>
                    {
                        s.Parser = ParserKind.Lazy;
                        s.UseDpBlockSplit = true;
                        s.UsePrediction = false;
                    }
                ),
                (
                    "Без ДП-розбиття блоків (фіксовані блоки)",
                    s =>
                    {
                        s.Parser = ParserKind.Lazy;
                        s.UseDpBlockSplit = false;
                        s.UsePrediction = false;
                    }
                ),
                (
                    "Жадібний розбір LZ77 (замість лінивого)",
                    s =>
                    {
                        s.Parser = ParserKind.Greedy;
                        s.UseDpBlockSplit = false;
                        s.UsePrediction = false;
                    }
                ),
                (
                    "Оптимальний ДП-розбір LZ77 (повноцінне ДП)",
                    s =>
                    {
                        s.Parser = ParserKind.Dp;
                        s.UseDpBlockSplit = false;
                        s.UsePrediction = false;
                    }
                ),
            };

            var sb = new StringBuilder();
            sb.AppendLine(
                "Конфігурація системи\tB_orig (Б)\tB_arc (Б)\tЧас t (с)\tS (Стиснення)\tT (Швидкодія)\tМетрика Q\tСтатус"
            );

            foreach (var (label, apply) in configs)
            {
                var settings = new Settings
                {
                    BlockSize = config.BlockSize,
                    MaxVolumeBytes = config.MaxVolumeBytes,
                };
                apply(settings);

                QEvaluation eval = Evaluate(corpus, settings, config, label, logPath);
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2}\t{3:F4}\t{4:F4}\t{5:F4}\t{6:F4}\t{7}",
                        eval.Name,
                        eval.OriginalBytes,
                        eval.ArchiveBytes,
                        eval.ElapsedSeconds,
                        eval.S,
                        eval.T,
                        eval.Q,
                        eval.RestoredExact ? "OK (CRC32 збігся)" : "ПОМИЛКА"
                    )
                );
            }

            return sb.ToString();
        }
    }
}
