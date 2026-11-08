using System.Text;

namespace SimpleArchiver.Tests
{
    // Просунуті рівні: ДП-розбір LZ77, ДП-розбиття на блоки, класифікатор типу файла, прогноз байта
    public class Stage3Tests
    {
        // ДП-розбір за фіксованими вартостями

        // Різні дані для перевірки розборів
        static List<byte[]> ParserSamples()
        {
            var samples = new List<byte[]>();
            samples.Add(TestHelpers.Hello480());
            samples.Add(
                Encoding.ASCII.GetBytes("xabQQQQabcdefgh##xabcdefgh, abcabcabcabc abcdefgh")
            );
            samples.Add(SyntheticFiles.Text(5000, 1));
            samples.Add(SyntheticFiles.Csv(5000, 2));
            samples.Add(TestHelpers.RandomBytes(3000, 3));
            samples.Add(new byte[] { 7, 7 });
            return samples;
        }

        // Кейс D1 номер 13: вартість ДП-розбору не більша за жадібний і лінивий
        [Fact]
        public void DpParseCostNeverHigherThanGreedyOrLazy()
        {
            foreach (byte[] data in ParserSamples())
            {
                List<Token> dp = Lz77.ParseDp(data, 64);

                Assert.True(Lz77.FixedCost(dp) <= Lz77.FixedCost(Lz77.ParseGreedy(data, 64)));
                Assert.True(Lz77.FixedCost(dp) <= Lz77.FixedCost(Lz77.ParseLazy(data, 64)));
                Assert.Equal(data, BlockCoder.Decode(BlockCoder.Encode(dp), data.Length));
            }
        }

        // Архів із ДП-розбором відновлює файли
        [Fact]
        public void DpParseArchiveRestoresFiles()
        {
            var settings = new Settings();
            settings.Parser = ParserKind.Dp;

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("text.txt", SyntheticFiles.Text(20000, 4)),
                TestHelpers.Password,
                settings
            );

            Assert.Equal(
                SyntheticFiles.Text(20000, 4),
                ArchiveReader.ReadArchive(volumes, TestHelpers.Password)[0].Data
            );
        }

        // ДП-розбиття на блоки

        // Текст, шум і нулі в одному файлі: різні частини краще стискаються окремими блоками
        static byte[] MixedData()
        {
            var data = new byte[20000];
            byte[] text = SyntheticFiles.Text(7000, 5);
            byte[] noise = TestHelpers.RandomBytes(7000, 6);
            Array.Copy(text, 0, data, 0, 7000);
            Array.Copy(noise, 0, data, 7000, 7000);
            return data; // останні 6000 байтів - нулі
        }

        // Налаштування для ДП-розбиття
        static Settings SplitSettings()
        {
            var settings = new Settings();
            settings.BlockSize = 8192;
            settings.SplitUnit = 1024;
            return settings;
        }

        // Кейс D1 номер 14: ДП-розбиття дає розмір не більший, ніж розбиття фіксованим блоком
        [Fact]
        public void DpSplitNeverBiggerThanFixedBlocks()
        {
            var samples = new List<byte[]>();
            samples.Add(MixedData());
            samples.Add(SyntheticFiles.Text(30000, 7));
            samples.Add(TestHelpers.RandomBytes(10000, 8));
            samples.Add(new byte[0]);

            foreach (byte[] data in samples)
            {
                long fixedSize = BlockSplitter.TotalSize(
                    BlockSplitter.SplitFixed(data, SplitSettings(), true)
                );
                long dpSize = BlockSplitter.TotalSize(BlockSplitter.SplitDp(data, SplitSettings()));
                Assert.True(dpSize <= fixedSize);
            }
        }

        // Архів із ДП-розбиттям відновлює файли
        [Fact]
        public void DpSplitArchiveRestoresFiles()
        {
            Settings settings = SplitSettings();
            settings.UseDpBlockSplit = true;

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("mixed.bin", MixedData()),
                TestHelpers.Password,
                settings
            );

            Assert.Equal(
                MixedData(),
                ArchiveReader.ReadArchive(volumes, TestHelpers.Password)[0].Data
            );
        }

        // BlockSize має ділитися на SplitUnit
        [Fact]
        public void DpSplitBlockSizeMustBeMultipleOfUnit()
        {
            var settings = new Settings();
            settings.BlockSize = 10000;
            settings.SplitUnit = 3000;

            bool rejected = false;
            try
            {
                BlockSplitter.SplitDp(MixedData(), settings);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Assert.True(rejected);
        }

        // Ознаки файла

        // Ознаки мають очікувані значення
        [Fact]
        public void FeaturesHaveExpectedValues()
        {
            Assert.Equal(52, FileFeatures.Compute(SyntheticFiles.Text(1000, 1)).Length);

            // Однакові байти - ентропія 0; усі 256 значень порівну - ентропія 8 бітів
            Assert.Equal(0.0, FileFeatures.ByteEntropy(new byte[1000]));

            var allBytes = new byte[256 * 4];
            for (int i = 0; i < allBytes.Length; i++)
                allBytes[i] = (byte)i;
            Assert.Equal(8.0, FileFeatures.ByteEntropy(allBytes), 6);

            Assert.Equal(
                1.0,
                FileFeatures.PrintableShare(Encoding.ASCII.GetBytes("Hello, world!\n"))
            );

            // Порожній файл - усі ознаки нульові
            foreach (double feature in FileFeatures.Compute(new byte[0]))
                Assert.Equal(0.0, feature);
        }

        // Перцептрон

        // Градієнт перцептрона збігається з чисельним
        [Fact]
        public void MlpGradientMatchesNumeric()
        {
            var net = new Mlp(new[] { FileFeatures.Count, 6, 7 }, 1);
            double[] x = FileFeatures.Compute(SyntheticFiles.Csv(2000, 1));
            var target = new double[7];
            target[2] = 1;

            Assert.True(net.GradientCheck(x, target) < 1e-7);
        }
    }
}
