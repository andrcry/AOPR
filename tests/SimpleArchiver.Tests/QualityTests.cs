namespace SimpleArchiver.Tests
{
    // Метрика Q: базове Q на корпусі (команда q) і таблиця впливу блоків (команда q-table)
    public class QualityTests
    {
        // Невеликий корпус для швидких тестів: текст, нулі і шум
        static List<ArchiveFile> SmallCorpus()
        {
            var corpus = new List<ArchiveFile>();
            corpus.Add(new ArchiveFile("text.txt", SyntheticFiles.Text(6000, 90)));
            corpus.Add(new ArchiveFile("zeros.dat", new byte[4000]));
            corpus.Add(new ArchiveFile("noise.bin", TestHelpers.RandomBytes(3000, 91)));
            return corpus;
        }

        // Тимчасовий файл для лога, якого ще немає
        static string TempLog()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "sarc-q-" + Guid.NewGuid().ToString("N") + ".log"
            );
        }

        // S, T і Q пораховані точно за формулами з розділу 4 завдання
        [Fact]
        public void QFollowsFormula()
        {
            string log = TempLog();
            try
            {
                var config = new TeamConfig();
                QEvaluation result = QualityRun.Measure(
                    SmallCorpus(),
                    new Settings(),
                    config,
                    "тест",
                    log
                );

                double s =
                    (double)result.OriginalBytes / (result.OriginalBytes + result.ArchiveBytes);
                double t = config.TRef / (config.TRef + result.ElapsedSeconds);

                Assert.True(result.RestoredExact);
                Assert.Equal(13000, result.OriginalBytes);
                Assert.Equal(s, result.S, 10);
                Assert.Equal(t, result.T, 10);
                Assert.Equal(config.Alpha * s + config.Beta * t, result.Q, 10);
            }
            finally
            {
                File.Delete(log);
            }
        }

        // Якщо архіватор не впорався, Q = 0, а в лог-файлі записано оброблену помилку
        [Fact]
        public void QIsZeroWhenArchiverFails()
        {
            string log = TempLog();
            try
            {
                var settings = new Settings();
                settings.BlockSize = 0; // неможливий розмір блока

                QEvaluation result = QualityRun.Measure(
                    SmallCorpus(),
                    settings,
                    new TeamConfig(),
                    "зламано",
                    log
                );

                Assert.False(result.RestoredExact);
                Assert.Equal(0, result.Q);
                Assert.Contains("[HANDLED_ERROR]", File.ReadAllText(log));
            }
            finally
            {
                File.Delete(log);
            }
        }

        // Команда q на корпусі викладача: 21 файл відновлено, рядок дописано в лог-файл
        [Fact]
        public void QOnTeacherCorpus()
        {
            string log = TempLog();
            try
            {
                string corpus = Path.Combine(TestHelpers.DataFolder(), "corpus");
                List<string> lines = QualityRun.Build(
                    new string[] { "q", "--log", log, corpus },
                    1
                );

                Assert.StartsWith("Метрика Q на корпусі: 21 файлів, 329903 байтів", lines[0]);
                Assert.EndsWith("\tтак", lines[4]);

                string[] logLines = File.ReadAllLines(log);
                Assert.Single(logLines);
                Assert.Contains("[OK]", logLines[0]);
                Assert.Contains("Files=21", logLines[0]);
            }
            finally
            {
                File.Delete(log);
            }
        }

        // Неправильна опція дає зрозумілу помилку
        [Fact]
        public void QRejectsUnknownOption()
        {
            bool rejected = false;
            try
            {
                QualityRun.Build(new string[] { "q", "--fast", "yes" }, 1);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Assert.True(rejected);
        }
    }
}
