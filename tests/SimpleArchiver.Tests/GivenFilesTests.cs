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
