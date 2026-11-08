using System.Text;

namespace SimpleArchiver.Tests
{
    // Етап 2: LZ77, рюкзак, томи
    public class Stage2Tests
    {
        // LZ77

        // Жадібний розбір знаходить елементи з прикладу специфікації
        [Fact]
        public void GreedyFindsSpecificationTokens()
        {
            List<Token> tokens = Lz77.ParseGreedy(TestHelpers.Hello480(), 64);

            // 12 літералів, потім два посилання на 12 байтів назад
            for (int i = 0; i < 12; i++)
                Assert.False(tokens[i].IsMatch);

            Assert.True(tokens[12].IsMatch);
            Assert.Equal(258, tokens[12].Length);
            Assert.Equal(12, tokens[12].Distance);
            Assert.True(tokens[13].IsMatch);
            Assert.Equal(210, tokens[13].Length);
            Assert.Equal(12, tokens[13].Distance);
        }

        // Лінивий розбір чекає довшого збігу
        [Fact]
        public void LazyWaitsForLongerMatch()
        {
            // В кінці "xabcdefgh": жадібний бере "xab", лінивий чекає на "abcdefgh"
            byte[] data = Encoding.ASCII.GetBytes("xabQQQQabcdefgh##xabcdefgh");

            List<Token> greedy = Lz77.ParseGreedy(data, 64);
            Token greedyFirst = greedy[greedy.Count - 2];
            Assert.True(greedyFirst.IsMatch);
            Assert.Equal(3, greedyFirst.Length);

            List<Token> lazy = Lz77.ParseLazy(data, 64);
            Token lazyLiteral = lazy[lazy.Count - 2];
            Token lazyMatch = lazy[lazy.Count - 1];
            Assert.False(lazyLiteral.IsMatch);
            Assert.Equal((byte)'x', lazyLiteral.Literal);
            Assert.True(lazyMatch.IsMatch);
            Assert.Equal(8, lazyMatch.Length);
        }

        // Обидва розбори відновлюють дані
        [Fact]
        public void GreedyAndLazyBothRestoreData()
        {
            var samples = new List<byte[]>();
            samples.Add(TestHelpers.Hello480());
            samples.Add(Encoding.UTF8.GetBytes("abababababab cdcdcdcd abab, тест тест тест тест!"));
            samples.Add(new byte[] { 1, 2 });
            samples.Add(SmallAlphabetData(50000, 7));

            foreach (byte[] data in samples)
            {
                byte[] greedyBlock = BlockCoder.Encode(Lz77.ParseGreedy(data, 64));
                byte[] lazyBlock = BlockCoder.Encode(Lz77.ParseLazy(data, 64));

                Assert.Equal(data, BlockCoder.Decode(greedyBlock, data.Length));
                Assert.Equal(data, BlockCoder.Decode(lazyBlock, data.Length));
            }
        }

        // Збіг на найбільшій відстані 32768 працює
        [Fact]
        public void Lz77MatchAtMaximumDistance()
        {
            // 32768 байтів шуму, потім перші 300 байтів ще раз - відстань рівно 32768
            byte[] noise = TestHelpers.RandomBytes(32768, 3);
            var data = new byte[32768 + 300];
            Array.Copy(noise, 0, data, 0, 32768);
            Array.Copy(noise, 0, data, 32768, 300);

            List<Token> tokens = Lz77.ParseGreedy(data, 64);

            bool foundFarMatch = false;
            foreach (Token token in tokens)
            {
                if (token.IsMatch && token.Distance == 32768)
                    foundFarMatch = true;
            }

            Assert.True(foundFarMatch);
            Assert.Equal(data, BlockCoder.Decode(BlockCoder.Encode(tokens), data.Length));
        }

        // Дані тільки з букв a, b, c, d - багато коротких повторів
        static byte[] SmallAlphabetData(int count, int seed)
        {
            var random = new Random(seed);
            var data = new byte[count];
            for (int i = 0; i < count; i++)
                data[i] = (byte)('a' + random.Next(4));

            return data;
        }

        // Рюкзак

        // ДП-рюкзак кращий за жадібний на класичному прикладі
        [Fact]
        public void KnapsackDpBeatsGreedyOnClassicExample()
        {
            // Місткість 10. Жадібний бере перший предмет (7/6) і більше нічого. ДП бере два інші
            int[] weights = { 6, 5, 5 };
            int[] values = { 7, 5, 5 };

            Assert.Equal(7, Knapsack.TotalValue(Knapsack.SolveGreedy(weights, values, 10), values));
            Assert.Equal(10, Knapsack.TotalValue(Knapsack.SolveDp(weights, values, 10), values));
        }

        // ДП-рюкзак точний і не гірший за жадібний
        [Fact]
        public void KnapsackDpIsExactAndNeverWorseThanGreedy()
        {
            var random = new Random(42);

            for (int test = 0; test < 200; test++)
            {
                int n = random.Next(1, 11);
                var weights = new int[n];
                var values = new int[n];
                for (int i = 0; i < n; i++)
                {
                    weights[i] = random.Next(1, 30);
                    values[i] = random.Next(1, 20);
                }
                int capacity = random.Next(0, 80);

                List<int> dp = Knapsack.SolveDp(weights, values, capacity);
                List<int> greedy = Knapsack.SolveGreedy(weights, values, capacity);

                Assert.True(Knapsack.TotalWeight(dp, weights) <= capacity);
                Assert.True(Knapsack.TotalWeight(greedy, weights) <= capacity);
                Assert.True(Knapsack.TotalValue(dp, values) >= Knapsack.TotalValue(greedy, values));
                Assert.Equal(
                    BruteForceBest(weights, values, capacity),
                    Knapsack.TotalValue(dp, values)
                );
            }
        }

        // Повний перебір усіх 2^n варіантів, щоб перевірити ДП
        static long BruteForceBest(int[] weights, int[] values, int capacity)
        {
            long best = 0;

            for (int mask = 0; mask < (1 << weights.Length); mask++)
            {
                long weight = 0;
                long value = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    if ((mask & (1 << i)) == 0)
                        continue;

                    weight += weights[i];
                    value += values[i];
                }

                if (weight <= capacity && value > best)
                    best = value;
            }

            return best;
        }

        // Томи

        // Налаштування з маленькими томами
        static Settings SmallVolumes()
        {
            var settings = new Settings();
            settings.BlockSize = 1000;
            settings.MaxVolumeBytes = 3000;
            return settings;
        }

        // Файли для перевірки томів
        static List<ArchiveFile> FilesForVolumes()
        {
            var files = new List<ArchiveFile>();
            files.Add(new ArchiveFile("noise.bin", TestHelpers.RandomBytes(10000, 5))); // більший за том
            files.Add(new ArchiveFile("hello.txt", TestHelpers.Hello480()));
            files.Add(new ArchiveFile("empty.txt", new byte[0]));
            return files;
        }

        // Великий файл ділиться, кожен том вміщується в ліміт
        [Fact]
        public void VolumesBigFileIsSplitAndEveryVolumeFits()
        {
            List<ArchiveFile> input = FilesForVolumes();
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                input,
                TestHelpers.Password,
                SmallVolumes()
            );

            Assert.True(volumes.Count > 1);
            foreach (byte[] volume in volumes)
                Assert.True(volume.Length <= 3000);

            List<ArchiveFile> output = ArchiveReader.ReadArchive(volumes, TestHelpers.Password);
            Assert.Equal(input.Count, output.Count);
            for (int i = 0; i < input.Count; i++)
            {
                Assert.Equal(input[i].Name, output[i].Name);
                Assert.Equal(input[i].Data, output[i].Data);
            }
        }

        // Порядок томів не важливий
        [Fact]
        public void VolumesOrderDoesNotMatter()
        {
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                FilesForVolumes(),
                TestHelpers.Password,
                SmallVolumes()
            );

            var reversed = new List<byte[]>(volumes);
            reversed.Reverse();

            List<ArchiveFile> output = ArchiveReader.ReadArchive(reversed, TestHelpers.Password);
            Assert.Equal(TestHelpers.RandomBytes(10000, 5), output[0].Data);
        }

        // Відсутній або дубльований том відхиляється
        [Fact]
        public void VolumesMissingOrDuplicatedVolumeIsRejected()
        {
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                FilesForVolumes(),
                TestHelpers.Password,
                SmallVolumes()
            );

            var missing = new List<byte[]>(volumes);
            missing.RemoveAt(1);
            Assert.Equal(
                ErrorCodes.Volumes,
                TestHelpers.ErrorWhenReading(missing, TestHelpers.Password)
            );

            var duplicated = new List<byte[]>(volumes);
            duplicated[1] = volumes[0];
            Assert.Equal(
                ErrorCodes.Volumes,
                TestHelpers.ErrorWhenReading(duplicated, TestHelpers.Password)
            );
        }

        // Томи з різних архівів відхиляються
        [Fact]
        public void VolumesFromDifferentArchivesAreRejected()
        {
            List<byte[]> first = ArchiveWriter.CreateArchive(
                FilesForVolumes(),
                TestHelpers.Password,
                SmallVolumes()
            );
            List<byte[]> second = ArchiveWriter.CreateArchive(
                FilesForVolumes(),
                TestHelpers.Password,
                SmallVolumes()
            );

            var mixed = new List<byte[]>(first);
            mixed[0] = second[0]; // інша сіль
            Assert.Equal(
                ErrorCodes.Volumes,
                TestHelpers.ErrorWhenReading(mixed, TestHelpers.Password)
            );
        }

        // Порожній архів має один том
        [Fact]
        public void VolumesEmptyArchiveHasOneVolume()
        {
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                new List<ArchiveFile>(),
                TestHelpers.Password,
                SmallVolumes()
            );

            Assert.Single(volumes);
            Assert.Empty(ArchiveReader.ReadArchive(volumes, TestHelpers.Password));
        }

        // Блок, більший за том, дає зрозумілу помилку
        [Fact]
        public void VolumesBlockBiggerThanVolumeGivesClearError()
        {
            var settings = new Settings();
            settings.BlockSize = 5000;
            settings.MaxVolumeBytes = 3000;

            bool failed = false;
            try
            {
                ArchiveWriter.CreateArchive(
                    TestHelpers.OneFile("noise.bin", TestHelpers.RandomBytes(5000, 9)),
                    TestHelpers.Password,
                    settings
                );
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }

            Assert.True(failed);
        }
    }
}
