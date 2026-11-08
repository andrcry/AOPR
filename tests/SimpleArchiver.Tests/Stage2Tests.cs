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
    }
}
