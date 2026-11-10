using System.Text;

namespace SimpleArchiver.Tests
{
    // Етап 1: CRC32, біти, Гаффман, кодування блока, архів, захист
    public class Stage1Tests
    {
        // CRC32 дає відомі значення
        [Fact]
        public void Crc32GivesKnownValues()
        {
            // Стандартна перевірка CRC32 і значення з контрольного прикладу
            Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
            Assert.Equal(0x70208846u, Crc32.Compute(TestHelpers.Hello480()));
        }

        // Числа записуються старшим байтом уперед
        [Fact]
        public void ByteWriterWritesBigEndian()
        {
            var writer = new ByteWriter();
            writer.WriteU16(0x1234);
            writer.WriteU32(0xA1B2C3D4);

            Assert.Equal(new byte[] { 0x12, 0x34, 0xA1, 0xB2, 0xC3, 0xD4 }, writer.ToArray());

            var reader = new ByteReader(writer.ToArray(), ErrorCodes.Payload);
            Assert.Equal(0x1234, reader.ReadU16());
            Assert.Equal(0xA1B2C3D4u, reader.ReadU32());
            Assert.Equal(0, reader.Remaining);
        }

        // Перший біт стає старшим, кінець добивається нулями
        [Fact]
        public void BitWriterFirstBitIsHighestAndPadsWithZeros()
        {
            var writer = new BitWriter();
            writer.WriteBit(1);
            writer.WriteBits(0b01, 2);

            // 1 0 1 і п'ять нулів = 1010 0000 = A0
            Assert.Equal(new byte[] { 0xA0 }, writer.ToArray());

            var reader = new BitReader(writer.ToArray());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0b01, reader.ReadBits(2));
            reader.CheckEndIsClean();
        }

        // Гаффман

        // Канонічні коди збігаються з прикладом специфікації
        [Fact]
        public void CanonicalCodesMatchSpecificationExample()
        {
            // Довжини з таблиці LIT розділу 8
            var lengths = new int[257];
            lengths[' '] = 3;
            lengths['d'] = 4;
            lengths['e'] = 4;
            lengths['h'] = 4;
            lengths['l'] = 2;
            lengths['o'] = 3;
            lengths['r'] = 4;
            lengths['w'] = 3;
            lengths[256] = 3;

            int[] codes = CanonicalCodes.Make(lengths);

            Assert.Equal(0b00, codes['l']);
            Assert.Equal(0b010, codes[' ']);
            Assert.Equal(0b011, codes['o']);
            Assert.Equal(0b100, codes['w']);
            Assert.Equal(0b101, codes[256]);
            Assert.Equal(0b1100, codes['d']);
            Assert.Equal(0b1101, codes['e']);
            Assert.Equal(0b1110, codes['h']);
            Assert.Equal(0b1111, codes['r']);
            Assert.True(CanonicalCodes.IsComplete(lengths));
        }

        // Довжини кодів Гаффмана збігаються з прикладом специфікації
        [Fact]
        public void HuffmanTreeGivesSpecificationLengths()
        {
            // Частоти з прикладу: "hello world " і два маркери посилань
            var counts = new long[257];
            foreach (char c in "hello world ")
                counts[c]++;
            counts[256] = 2;

            int[] lengths = HuffmanTree.BuildCodeLengths(counts);

            Assert.Equal(2, lengths['l']);
            Assert.Equal(3, lengths[' ']);
            Assert.Equal(3, lengths['o']);
            Assert.Equal(3, lengths['w']);
            Assert.Equal(3, lengths[256]);
            Assert.Equal(4, lengths['d']);
            Assert.Equal(4, lengths['e']);
            Assert.Equal(4, lengths['h']);
            Assert.Equal(4, lengths['r']);
        }

        // Єдиний символ отримує довжину 1
        [Fact]
        public void HuffmanTreeOneSymbolGetsLengthOne()
        {
            var counts = new long[257];
            counts['A'] = 5000;

            Assert.Equal(1, HuffmanTree.BuildCodeLengths(counts)['A']);
        }

        // Коди не довші за 15 бітів
        [Fact]
        public void HuffmanTreeCodesNeverLongerThan15()
        {
            // Частоти Фібоначчі дають дуже глибоке дерево
            var counts = new long[30];
            long a = 1;
            long b = 1;
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = a;
                long next = a + b;
                a = b;
                b = next;
            }

            int[] lengths = HuffmanTree.BuildCodeLengths(counts);

            foreach (int len in lengths)
                Assert.True(len >= 1 && len <= 15);
            Assert.True(CanonicalCodes.IsComplete(lengths));
        }

        // Неправильні таблиці кодів відхиляються
        [Fact]
        public void CodeTableRejectsBadTables()
        {
            // DIST: символ 16 недопустимий
            var badDistance = new ByteWriter();
            badDistance.WriteU16(2);
            badDistance.WriteU16(0);
            badDistance.WriteU8(1);
            badDistance.WriteU16(16);
            badDistance.WriteU8(1);
            Assert.Equal(ErrorCodes.Code, ErrorWhenReadingTable(badDistance.ToArray(), 16));

            // Довжини 1 і 2 - код неповний, Крафт порушено
            var notComplete = new ByteWriter();
            notComplete.WriteU16(2);
            notComplete.WriteU16(0);
            notComplete.WriteU8(1);
            notComplete.WriteU16(1);
            notComplete.WriteU8(2);
            Assert.Equal(ErrorCodes.Code, ErrorWhenReadingTable(notComplete.ToArray(), 257));

            // Довжина 16 більша за дозволені 15
            var tooLong = new ByteWriter();
            tooLong.WriteU16(1);
            tooLong.WriteU16(0);
            tooLong.WriteU8(16);
            Assert.Equal(ErrorCodes.Code, ErrorWhenReadingTable(tooLong.ToArray(), 257));
        }

        // Пробує прочитати таблицю і повертає код помилки
        static string ErrorWhenReadingTable(byte[] table, int alphabetSize)
        {
            try
            {
                CodeTable.Read(new ByteReader(table, ErrorCodes.Code), alphabetSize);
            }
            catch (ArchiveException e)
            {
                return e.Code;
            }

            return "OK";
        }

        // Кодування блока

        // Елементи блока hello.txt з розділу 8 специфікації: 12 літералів "hello world "
        // і два посилання на 12 байтів назад. На чекпоінті 1 пошуку повторів ще немає,
        // тому елементи складено вручну, щоб перевірити саме формат блока і коди Гаффмана
        static List<Token> SpecificationTokens()
        {
            var tokens = new List<Token>();
            foreach (char c in "hello world ")
                tokens.Add(Token.MakeLiteral((byte)c));
            tokens.Add(Token.MakeMatch(258, 12));
            tokens.Add(Token.MakeMatch(210, 12));
            return tokens;
        }

        // Блок hello.txt збігається з прикладом специфікації
        [Fact]
        public void BlockCoderMatchesSpecificationExample()
        {
            List<Token> tokens = SpecificationTokens();
            byte[] block = BlockCoder.Encode(tokens);

            Assert.Equal(14, tokens.Count);
            Assert.Equal(57, block.Length);

            // Останні 7 байтів - бітовий потік ED 06 A3 F3 15 9D 18
            var stream = new byte[7];
            Array.Copy(block, block.Length - 7, stream, 0, 7);
            Assert.Equal(new byte[] { 0xED, 0x06, 0xA3, 0xF3, 0x15, 0x9D, 0x18 }, stream);

            Assert.Equal(TestHelpers.Hello480(), BlockCoder.Decode(block, 480));
        }

        // Методичка 10.15: декодер на вручну складених елементах
        [Fact]
        public void BlockCoderDecodesHandMadeTokens()
        {
            var tokens = new List<Token>();
            tokens.Add(Token.MakeLiteral((byte)'a'));
            tokens.Add(Token.MakeLiteral((byte)'b'));
            tokens.Add(Token.MakeLiteral((byte)'c'));
            tokens.Add(Token.MakeMatch(6, 3));

            byte[] block = BlockCoder.Encode(tokens);

            Assert.Equal(Encoding.ASCII.GetBytes("abcabcabc"), BlockCoder.Decode(block, 9));
        }

        // Методичка 10.20: відстань 32767 і 32768 дозволені, 32769 ні
        [Fact]
        public void BlockCoderDistanceLimits()
        {
            byte[] start = TestHelpers.RandomBytes(32768, 11);
            List<Token> tokens = BlockCoder.LiteralsOnly(start);
            tokens.Add(Token.MakeMatch(3, 32768));
            tokens.Add(Token.MakeMatch(3, 32767));

            byte[] restored = BlockCoder.Decode(BlockCoder.Encode(tokens), 32768 + 6);

            Assert.Equal(start[0], restored[32768]);
            Assert.Equal(start[1], restored[32769]);
            Assert.Equal(start[2], restored[32770]);

            List<Token> tooFar = BlockCoder.LiteralsOnly(TestHelpers.RandomBytes(32769, 12));
            tooFar.Add(Token.MakeMatch(3, 32769));

            bool rejected = false;
            try
            {
                BlockCoder.Encode(tooFar);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Assert.True(rejected);

            // Відстань 32769 потребувала б символу DIST 16, а таку таблицю розархіватор відхиляє
            Assert.Equal(15, BlockCoder.DistanceCode(32768));
            Assert.Equal(16, BlockCoder.DistanceCode(32769));
        }

        // Блок із самих літералів відновлюється
        [Fact]
        public void BlockCoderLiteralsOnlyRoundTrip()
        {
            byte[] data = Encoding.UTF8.GetBytes("Привіт, світе! Hello!");
            byte[] block = BlockCoder.Encode(BlockCoder.LiteralsOnly(data));

            Assert.Equal(data, BlockCoder.Decode(block, data.Length));
        }

        // Цілий архів

        // Архів hello.txt має 172 байти, як у специфікації
        [Fact]
        public void ArchiveSpecificationExampleIs172Bytes()
        {
            byte[] salt =
            {
                0x00,
                0x01,
                0x02,
                0x03,
                0x04,
                0x05,
                0x06,
                0x07,
                0x08,
                0x09,
                0x0A,
                0x0B,
                0x0C,
                0x0D,
                0x0E,
                0x0F,
            };
            byte[] nonce =
            {
                0x01,
                0x02,
                0x03,
                0x04,
                0x05,
                0x06,
                0x07,
                0x08,
                0x00,
                0x00,
                0x00,
                0x00,
            };

            // Навантаження як у розділі 8: один файл hello.txt, один блок з елементами прикладу
            var file = new FileRecord();
            file.Name = "hello.txt";
            file.OriginalSize = 480;
            file.Crc = Crc32.Compute(TestHelpers.Hello480());
            file.FileType = 0;
            file.Strategy = 1;

            var block = new BlockRecord();
            block.RawLength = 480;
            block.Data = BlockCoder.Encode(SpecificationTokens());

            var payload = new Payload();
            payload.Files.Add(file);
            payload.Blocks.Add(block);
            Assert.Equal(99, payload.ToBytes().Length);

            var volumes = new List<byte[]>();
            volumes.Add(
                Volume.Seal(
                    0,
                    1,
                    salt,
                    nonce,
                    payload.ToBytes(),
                    Keys.Derive(TestHelpers.Password, salt)
                )
            );

            Assert.Equal(172, volumes[0].Length);

            // Заголовок байт у байт як у розділі 8
            byte[] expectedHeader =
            {
                0x53,
                0x41,
                0x52,
                0x43,
                0x01,
                0x00,
                0x00,
                0x00,
                0x01,
                0x00,
                0x01,
                0x02,
                0x03,
                0x04,
                0x05,
                0x06,
                0x07,
                0x08,
                0x09,
                0x0A,
                0x0B,
                0x0C,
                0x0D,
                0x0E,
                0x0F,
                0x01,
                0x02,
                0x03,
                0x04,
                0x05,
                0x06,
                0x07,
                0x08,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x63,
            };
            var header = new byte[41];
            Array.Copy(volumes[0], header, 41);
            Assert.Equal(expectedHeader, header);

            // Розархіватора ще немає, тому розшифровуємо том і читаємо навантаження вручну
            VolumeHeader parsed = VolumeHeader.Parse(volumes[0]);
            Payload restored = Payload.Parse(
                Volume.Open(volumes[0], parsed, Keys.Derive(TestHelpers.Password, salt))
            );
            Assert.Equal("hello.txt", restored.Files[0].Name);
            Assert.Equal(TestHelpers.Hello480(), BlockCoder.Decode(restored.Blocks[0].Data, 480));
        }
    }
}
