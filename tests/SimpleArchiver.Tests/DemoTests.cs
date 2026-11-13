namespace SimpleArchiver.Tests
{
    // Демо-дані замість файлів викладача і перевірка еталонних архівів командою ref-check
    public class DemoTests
    {
        // Створює демо-дані в новій тимчасовій папці
        static string MakeDemo(int seed)
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "sarc-demo-" + Guid.NewGuid().ToString("N")
            );
            DemoData.Create(folder, seed);
            return folder;
        }

        // Усі 6 еталонних і 6 зіпсованих демо-архівів проходять перевірку
        [Fact]
        public void DemoReferencesPassCheck()
        {
            string folder = MakeDemo(2026);
            try
            {
                ReferenceCheck.Build(
                    Path.Combine(folder, "reference"),
                    out int passed,
                    out int total
                );

                Assert.Equal(12, total);
                Assert.Equal(12, passed);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // ref_01 зібрано з параметрами контрольного прикладу: розмір 172 байти, як у розділі 8 специфікації
        [Fact]
        public void DemoRef01MatchesSpecExample()
        {
            string folder = MakeDemo(2026);
            try
            {
                byte[] archive = File.ReadAllBytes(
                    Path.Combine(folder, "reference", "ref_01_single_file.arc")
                );
                Assert.Equal(172, archive.Length);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // Неправильний очікуваний код помічається: перевірка не завжди каже OK
        [Fact]
        public void ReferenceCheckNoticesWrongCode()
        {
            string folder = MakeDemo(2026);
            try
            {
                string reference = Path.Combine(folder, "reference");
                string path = Path.Combine(reference, "expected_failures.json");
                File.WriteAllText(
                    path,
                    File.ReadAllText(path).Replace("\"E_MAGIC\"", "\"E_HMAC\"")
                );

                ReferenceCheck.Build(reference, out int passed, out int total);

                Assert.Equal(12, total);
                Assert.Equal(11, passed);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // train/ і valid/ мають 350 і 105 файлів усіх семи типів, як у викладача
        [Fact]
        public void DemoSamplesHaveAllTypes()
        {
            string folder = MakeDemo(2026);
            try
            {
                List<Sample> train = Dataset.Load(Path.Combine(folder, "train"));
                List<Sample> valid = Dataset.Load(Path.Combine(folder, "valid"));

                Assert.Equal(350, train.Count);
                Assert.Equal(105, valid.Count);

                var counts = new int[FileClassifier.ClassCount];
                foreach (Sample sample in train)
                    counts[sample.Label]++;
                foreach (int count in counts)
                    Assert.Equal(50, count);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // З тим самим seed виходять ті самі файли, з іншим seed - інші
        [Fact]
        public void DemoCorpusDependsOnSeed()
        {
            string first = MakeDemo(1);
            string second = MakeDemo(1);
            string third = MakeDemo(2);
            try
            {
                string file = Path.Combine("corpus", "text", "text_000.txt");
                byte[] a = File.ReadAllBytes(Path.Combine(first, file));
                byte[] b = File.ReadAllBytes(Path.Combine(second, file));
                byte[] c = File.ReadAllBytes(Path.Combine(third, file));

                Assert.Equal(a, b);
                Assert.NotEqual(a, c);
            }
            finally
            {
                Directory.Delete(first, true);
                Directory.Delete(second, true);
                Directory.Delete(third, true);
            }
        }

        // Непорожню папку команда не чіпає
        [Fact]
        public void DemoRefusesNonEmptyFolder()
        {
            string folder = MakeDemo(2026);
            try
            {
                bool refused = false;
                try
                {
                    DemoData.Create(folder, 2026);
                }
                catch (IOException)
                {
                    refused = true;
                }
                Assert.True(refused);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
