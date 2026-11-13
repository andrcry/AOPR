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

        // Таблицю впливу кожного блока на Q тепер будує клас BlockImpactTable (команда q-table)
    }
}
