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
    }
}
