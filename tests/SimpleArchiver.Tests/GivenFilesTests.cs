namespace SimpleArchiver.Tests
{
    // Перевірки на файлах викладача з папки data/
    public class GivenFilesTests
    {
        // Папка data/ проєкту. Тести запускаються з tests/.../bin/..., тому піднімаємося вгору, доки не знайдемо SimpleArchiver.slnx
        static string DataFolder()
        {
            string? folder = AppContext.BaseDirectory;
            while (folder != null && !File.Exists(Path.Combine(folder, "SimpleArchiver.slnx")))
                folder = Path.GetDirectoryName(folder);

            if (folder == null)
                throw new DirectoryNotFoundException(
                    "не знайдено папку проєкту з SimpleArchiver.slnx"
                );

            return Path.Combine(folder, "data");
        }

        // train/ і valid/ мають 350 і 105 файлів, по 50 і 15 кожного з семи типів
        [Fact]
        public void TeacherLabelsAreRead()
        {
            Dictionary<string, int> train = Dataset.ReadLabels(
                Path.Combine(DataFolder(), "train", "labels.csv")
            );
            Dictionary<string, int> valid = Dataset.ReadLabels(
                Path.Combine(DataFolder(), "valid", "labels.csv")
            );

            Assert.Equal(350, train.Count);
            Assert.Equal(105, valid.Count);

            var counts = new int[FileClassifier.ClassCount];
            foreach (int type in train.Values)
                counts[type]++;
            foreach (int count in counts)
                Assert.Equal(50, count);

            // Рядок "f0101.pgm,3,растр без стиснення,16192": тип береться з колонки type, а не з останньої
            Assert.Equal(3, train["f0101.pgm"]);
        }

        // Усі файли з labels.csv є в папці
        [Fact]
        public void TeacherSampleFilesExist()
        {
            string[] folders = { "train", "valid", "corpus" };
            foreach (string name in folders)
            {
                string folder = Path.Combine(DataFolder(), name);
                foreach (string file in Dataset.ReadLabels(Path.Combine(folder, "labels.csv")).Keys)
                    Assert.True(File.Exists(Path.Combine(folder, file)), file);
            }
        }

        // У корпусі 21 файл. labels.csv - опис корпусу, тому в список файлів не потрапляє
        [Fact]
        public void CorpusDoesNotIncludeLabels()
        {
            List<string> files = InputFiles.Collect(
                new string[] { Path.Combine(DataFolder(), "corpus") },
                0
            );

            Assert.Equal(21, files.Count);
            foreach (string file in files)
                Assert.NotEqual("labels.csv", Path.GetFileName(file));
        }

        // team_config.json читається: розмір блока і тому, пріоритети за назвами типів, метрика Q, F1_min
        [Fact]
        public void TeamConfigIsRead()
        {
            TeamConfig config = TeamConfig.Load(Path.Combine(DataFolder(), "team_config.json"));

            Assert.Equal("T01", config.Team);
            Assert.Equal(2001, config.Seed);
            Assert.Equal("algo2026", config.Password);
            Assert.Equal(16384, config.BlockSize);
            Assert.Equal(65536, config.MaxVolumeBytes);
            Assert.Equal(new int[] { 3, 3, 2, 2, 2, 1, 1 }, config.TypePriorities);
            Assert.Equal(0.7, config.Alpha);
            Assert.Equal(0.3, config.Beta);
            Assert.Equal(30.0, config.TRef);
            Assert.Equal(0.61, config.F1Min);
            Assert.Equal(3, config.Initializations);
        }

        // Значення за замовчуванням у Settings і TeamConfig такі самі, як у файлі викладача.
        // Тоді тести і програма без файла працюють з тими самими параметрами
        [Fact]
        public void DefaultsMatchTeamConfig()
        {
            TeamConfig fromFile = TeamConfig.Load(Path.Combine(DataFolder(), "team_config.json"));
            var defaults = new TeamConfig();
            var settings = new Settings();

            Assert.Equal(fromFile.BlockSize, settings.BlockSize);
            Assert.Equal(fromFile.MaxVolumeBytes, settings.MaxVolumeBytes);
            Assert.Equal(fromFile.TypePriorities, settings.TypePriorities);
            Assert.Equal(fromFile.Alpha, defaults.Alpha);
            Assert.Equal(fromFile.TRef, defaults.TRef);
            Assert.Equal(fromFile.F1Min, defaults.F1Min);
            Assert.Equal(0, settings.BlockSize % settings.SplitUnit);
        }

        // Вартості ДП з team_config.json збігаються з тими, що в коді ДП-розбору
        [Fact]
        public void DpCostsMatchTeamConfig()
        {
            TeamConfig config = TeamConfig.Load(Path.Combine(DataFolder(), "team_config.json"));

            Assert.Equal(Lz77.LiteralCost, config.LiteralBits);
            Assert.Equal(Lz77.MatchCost(1), config.ReferenceBits);
        }

        // Пошкоджений team_config.json дає зрозумілу помилку, а не падіння програми
        [Fact]
        public void BrokenTeamConfigIsReported()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{ \"команда\": \"T01\" }");
                bool reported = false;
                try
                {
                    TeamConfig.Load(path);
                }
                catch (InvalidOperationException)
                {
                    reported = true;
                }
                Assert.True(reported);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // Усі 6 еталонних архівів викладача розпаковуються і збігаються з описом,
        // а всі 6 зіпсованих відхиляються саме з тими кодами, що в expected_failures.json
        [Fact]
        public void TeacherReferencesPass()
        {
            ReferenceCheck.Build(
                Path.Combine(DataFolder(), "reference"),
                out int passed,
                out int total
            );

            Assert.Equal(12, total);
            Assert.Equal(12, passed);
        }

        // Наш пакувальник з тими самими сіллю, nonce і жадібним розбором дає ref_01 байт у байт
        [Fact]
        public void Ref01IsSameByteForByte()
        {
            byte[] teacher = File.ReadAllBytes(
                Path.Combine(DataFolder(), "reference", "ref_01_single_file.arc")
            );
            var volumes = new List<byte[]>();
            volumes.Add(teacher);
            List<ArchiveFile> files = ArchiveReader.ReadArchive(volumes, "algo2026");

            var salt = new byte[16];
            for (int i = 0; i < 16; i++)
                salt[i] = (byte)i;
            byte[] noncePrefix = { 1, 2, 3, 4, 5, 6, 7, 8 };

            var settings = new Settings();
            settings.Parser = ParserKind.Greedy;
            List<byte[]> ours = ArchiveWriter.CreateArchive(
                files,
                "algo2026",
                settings,
                salt,
                noncePrefix
            );

            Assert.Single(ours);
            Assert.Equal(teacher, ours[0]);
        }

        // Порожній файл записується одним блоком raw_len = 0 без стиснення, як у ref_02_empty_file,
        // і архів має той самий розмір 115 байтів
        [Fact]
        public void EmptyFileIsLikeRef02()
        {
            var files = new List<ArchiveFile>();
            files.Add(new ArchiveFile("empty.dat", new byte[0]));

            List<Payload> payloads = ArchiveWriter.BuildPayloads(files, new Settings());
            Assert.Single(payloads);
            Assert.Single(payloads[0].Blocks);
            Assert.Equal(0, payloads[0].Blocks[0].RawLength);
            Assert.Empty(payloads[0].Blocks[0].Data);
            Assert.Equal(BlockRecord.NoCompressionFlag, payloads[0].Blocks[0].Flags);

            byte[] teacher = File.ReadAllBytes(
                Path.Combine(DataFolder(), "reference", "ref_02_empty_file.arc")
            );
            List<byte[]> ours = ArchiveWriter.CreateArchive(files, "algo2026", new Settings());
            Assert.Equal(teacher.Length, ours[0].Length);
            Assert.Empty(ArchiveReader.ReadArchive(ours, "algo2026")[0].Data);
        }

        // Порожній файл поруч із великими файлами теж відновлюється, навіть якщо томів кілька
        [Fact]
        public void EmptyFileInManyVolumes()
        {
            var files = new List<ArchiveFile>();
            files.Add(new ArchiveFile("noise.bin", TestHelpers.RandomBytes(150000, 80)));
            files.Add(new ArchiveFile("empty.dat", new byte[0]));

            List<byte[]> volumes = ArchiveWriter.CreateArchive(files, "algo2026", new Settings());
            List<ArchiveFile> restored = ArchiveReader.ReadArchive(volumes, "algo2026");

            Assert.True(volumes.Count > 1);
            Assert.Equal(2, restored.Count);
            foreach (ArchiveFile file in restored)
            {
                if (file.Name == "empty.dat")
                    Assert.Empty(file.Data);
                else
                    Assert.Equal(150000, file.Data.Length);
            }
        }

        // Якщо бракує тому, перевірка це помічає
        [Fact]
        public void MissingVolumeIsNoticed()
        {
            string reference = Path.Combine(DataFolder(), "reference");
            string folder = Path.Combine(
                Path.GetTempPath(),
                "sarc-ref-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(folder);
            try
            {
                string[] names =
                {
                    "ref_06_two_volumes.json",
                    "ref_06_two_volumes.part0.arc",
                    "ref_06_two_volumes.part1.arc",
                };
                foreach (string name in names)
                    File.Copy(Path.Combine(reference, name), Path.Combine(folder, name));

                List<string> lines = ReferenceCheck.Build(folder, out int passed, out int total);

                Assert.Equal(0, passed);
                Assert.Contains("знайдено томів 2, а в .json 3", lines[2]);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // entropy-table ділить корпус на сім груп за labels.csv
        [Fact]
        public void EntropyTableSplitsCorpusByLabels()
        {
            List<string> lines = EntropyTable.Build(
                new string[] { "entropy-table", Path.Combine(DataFolder(), "corpus") },
                1
            );

            Assert.Equal(2 + FileClassifier.ClassCount, lines.Count);
            for (int type = 0; type < FileClassifier.ClassCount; type++)
                Assert.StartsWith(FileClassifier.classNames[type] + "\t3\t", lines[2 + type]);
        }

        // measure додає таблицю за групами, якщо в папці є labels.csv
        [Fact]
        public void MeasureTableHasGroups()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "sarc-measure-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "a.txt"), SyntheticFiles.Text(3000, 81));
                File.WriteAllBytes(Path.Combine(folder, "b.txt"), SyntheticFiles.Text(2000, 82));
                File.WriteAllBytes(Path.Combine(folder, "z.dat"), new byte[1000]);
                File.WriteAllText(
                    Path.Combine(folder, "labels.csv"),
                    "file,type,type_name,size\na.txt,0,x,3000\nb.txt,0,x,2000\nz.dat,6,x,1000\n"
                );

                List<string> lines = MeasureTable.Build(new string[] { "measure", folder }, 1);
                int header = lines.IndexOf("Розбір LZ77 за групами корпусу (групи з labels.csv)");

                Assert.True(header > 0);
                Assert.StartsWith("природний текст\t2\t5000\t", lines[header + 2]);
                Assert.StartsWith("штучні дані\t1\t1000\t", lines[header + 3]);
                Assert.Equal(header + 4, lines.Count);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // knapsack-table без класифікатора бере тип з labels.csv, тому цінність блока - пріоритет його типу
        [Fact]
        public void KnapsackTableUsesLabels()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "sarc-knapsack-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllBytes(
                    Path.Combine(folder, "packed.z"),
                    TestHelpers.RandomBytes(1000, 83)
                );
                File.WriteAllText(
                    Path.Combine(folder, "labels.csv"),
                    "file,type,type_name,size\npacked.z,5,вже стиснуте,1000\n"
                );

                List<string> lines = KnapsackTable.Build(
                    new string[] { "knapsack-table", folder },
                    1
                );

                // Один блок типу 5 "вже стиснуте" з пріоритетом 1 з team_config.json
                Assert.StartsWith("ДП\t1\t", lines[2]);
                Assert.EndsWith("\t1", lines[2].Substring(0, lines[2].LastIndexOf('\t')));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // labels.csv без рядка з назвами колонок теж читається: ім'я - перша колонка, тип - друга
        [Fact]
        public void LabelsWithoutHeaderAreRead()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "a.txt,0\nb.bin,5\n");
                Dictionary<string, int> labels = Dataset.ReadLabels(path);

                Assert.Equal(2, labels.Count);
                Assert.Equal(5, labels["b.bin"]);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
