namespace SimpleArchiver.Tests
{
    // Команди таблиць для звіту: розгортання папок, таблиця рюкзака, таблиця ентропії залишків
    public class TableTests
    {
        // Створює порожню тимчасову папку для тесту
        static string MakeTempFolder()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "sarc-test-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(folder);
            return folder;
        }

        // Повертає значення з рядка таблиці виду "назва<табуляція>значення"
        static string ValueOf(List<string> lines, string name)
        {
            foreach (string line in lines)
            {
                if (line.StartsWith(name + "\t"))
                    return line.Substring(name.Length + 1);
            }
            return "";
        }

        // Папка розгортається у файли за абеткою, приховані файли пропускаються
        [Fact]
        public void InputFilesExpandsFoldersAndSkipsHidden()
        {
            string folder = MakeTempFolder();
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "sub"));
                File.WriteAllBytes(Path.Combine(folder, "b.txt"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(folder, "sub", "a.txt"), new byte[] { 2 });
                File.WriteAllBytes(Path.Combine(folder, ".gitkeep"), new byte[0]);

                List<string> files = InputFiles.Collect(new string[] { "command", folder }, 1);

                Assert.Equal(2, files.Count);
                Assert.EndsWith("b.txt", files[0]);
                Assert.EndsWith("a.txt", files[1]);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // ДП-рюкзак у таблиці не гірший за жадібний і не перевищує місткість тому
        [Fact]
        public void KnapsackTableDpNotWorseThanGreedy()
        {
            string folder = MakeTempFolder();
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "a.bin"), TestHelpers.RandomBytes(4000, 1));
                File.WriteAllBytes(Path.Combine(folder, "b.bin"), TestHelpers.RandomBytes(3000, 2));
                File.WriteAllBytes(Path.Combine(folder, "c.bin"), TestHelpers.RandomBytes(3000, 3));

                string[] args = { "knapsack-table", "--volume", "7000", folder };
                List<string> lines = KnapsackTable.Build(args, 1);

                Assert.Equal("3", ValueOf(lines, "файлів"));
                Assert.Equal("3", ValueOf(lines, "блоків_всього"));
                Assert.True(long.Parse(ValueOf(lines, "різниця_цінності")) >= 0);

                // ДП бере два менші блоки (3015 + 3015 байтів), бо вони вміщуються разом
                string[] dpRow = lines[2].Split('\t');
                Assert.Equal("ДП", dpRow[0]);
                Assert.Equal("2", dpRow[1]);
                Assert.True(
                    long.Parse(dpRow[2]) <= long.Parse(ValueOf(lines, "місткість_тому_байтів"))
                );
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
