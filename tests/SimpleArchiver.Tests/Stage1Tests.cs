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

        // Блок hello.txt збігається з прикладом специфікації
        [Fact]
        public void BlockCoderMatchesSpecificationExample()
        {
            List<Token> tokens = Lz77.ParseGreedy(TestHelpers.Hello480(), 64);
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
            List<Token> tokens = Lz77.ParseLiteralsOnly(start);
            tokens.Add(Token.MakeMatch(3, 32768));
            tokens.Add(Token.MakeMatch(3, 32767));

            byte[] restored = BlockCoder.Decode(BlockCoder.Encode(tokens), 32768 + 6);

            Assert.Equal(start[0], restored[32768]);
            Assert.Equal(start[1], restored[32769]);
            Assert.Equal(start[2], restored[32770]);

            List<Token> tooFar = Lz77.ParseLiteralsOnly(TestHelpers.RandomBytes(32769, 12));
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
            byte[] block = BlockCoder.Encode(Lz77.ParseLiteralsOnly(data));

            Assert.Equal(data, BlockCoder.Decode(block, data.Length));
        }

        // Шум зберігається без стиснення
        [Fact]
        public void RandomNoiseIsStoredWithoutCompression()
        {
            byte[] noise = TestHelpers.RandomBytes(10000, 1);
            PreparedBlock block = ArchiveWriter.EncodeBlock(noise, TestHelpers.DefaultSettings());

            Assert.Equal(BlockRecord.NoCompressionFlag, block.Flags);
            Assert.Equal(noise.Length, block.Data.Length);
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
            byte[] noncePrefix = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            var settings = TestHelpers.DefaultSettings();
            settings.Parser = ParserKind.Greedy; // у прикладі strategy = 1

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("hello.txt", TestHelpers.Hello480()),
                TestHelpers.Password,
                settings,
                salt,
                noncePrefix
            );

            Assert.Single(volumes);
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

            List<ArchiveFile> files = ArchiveReader.ReadArchive(volumes, TestHelpers.Password);
            Assert.Equal("hello.txt", files[0].Name);
            Assert.Equal(TestHelpers.Hello480(), files[0].Data);
        }

        // Текстовий файл відновлюється побайтово
        [Fact]
        public void ArchiveTextFileRestoredByteForByte()
        {
            byte[] text = Encoding.UTF8.GetBytes(
                "Алгоритми і оптимізація. Алгоритми і оптимізація. Кінець."
            );
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("text.txt", text),
                TestHelpers.Password,
                TestHelpers.DefaultSettings()
            );

            List<ArchiveFile> files = ArchiveReader.ReadArchive(volumes, TestHelpers.Password);

            Assert.Single(files);
            Assert.Equal("text.txt", files[0].Name);
            Assert.Equal(text, files[0].Data);
        }

        // Порожній файл і файл з одного байта відновлюються
        [Fact]
        public void ArchiveEmptyFileAndOneByteFile()
        {
            var input = new List<ArchiveFile>();
            input.Add(new ArchiveFile("empty.bin", new byte[0]));
            input.Add(new ArchiveFile("one.bin", new byte[] { 42 }));

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                input,
                TestHelpers.Password,
                TestHelpers.DefaultSettings()
            );
            List<ArchiveFile> output = ArchiveReader.ReadArchive(volumes, TestHelpers.Password);

            Assert.Equal(2, output.Count);
            Assert.Equal("empty.bin", output[0].Name);
            Assert.Empty(output[0].Data);
            Assert.Equal(new byte[] { 42 }, output[1].Data);
        }

        // Однакові байти стискаються більш ніж у 10 разів
        [Fact]
        public void ArchiveSameBytesCompressManyTimes()
        {
            var data = new byte[100000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)'A';

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("aaa.txt", data),
                TestHelpers.Password,
                TestHelpers.DefaultSettings()
            );

            Assert.True(volumes[0].Length * 10 < data.Length);
            Assert.Equal(data, ArchiveReader.ReadArchive(volumes, TestHelpers.Password)[0].Data);
        }

        // Методичка 10.63: файл з нулів і файл коротший за MIN_MATCH
        [Fact]
        public void ArchiveAllZeroFileAndTinyFile()
        {
            var input = new List<ArchiveFile>();
            input.Add(new ArchiveFile("zeros.bin", new byte[20000]));
            input.Add(new ArchiveFile("two.txt", Encoding.ASCII.GetBytes("ab")));

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                input,
                TestHelpers.Password,
                TestHelpers.DefaultSettings()
            );
            List<ArchiveFile> output = ArchiveReader.ReadArchive(volumes, TestHelpers.Password);

            Assert.Equal(new byte[20000], output[0].Data);
            Assert.Equal(Encoding.ASCII.GetBytes("ab"), output[1].Data);
            Assert.True(volumes[0].Length < 1000);
        }

        // Захист

        // Невірний пароль дає E_HMAC
        [Fact]
        public void ArchiveWrongPasswordGivesHmacError()
        {
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("a.txt", TestHelpers.Hello480()),
                TestHelpers.Password,
                TestHelpers.DefaultSettings()
            );

            Assert.Equal(ErrorCodes.Hmac, TestHelpers.ErrorWhenReading(volumes, "wrong-password"));
        }

        // Змінений байт дає E_HMAC
        [Fact]
        public void ArchiveChangedByteGivesHmacError()
        {
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("a.txt", TestHelpers.Hello480()),
                TestHelpers.Password,
                TestHelpers.DefaultSettings()
            );

            // Змінений байт шифротексту
            byte[] changedData = TestHelpers.Copy(volumes[0]);
            changedData[50] ^= 0x01;
            Assert.Equal(
                ErrorCodes.Hmac,
                TestHelpers.ErrorWhenReading(changedData, TestHelpers.Password)
            );

            // Змінений байт пломби
            byte[] changedTag = TestHelpers.Copy(volumes[0]);
            changedTag[changedTag.Length - 1] ^= 0x01;
            Assert.Equal(
                ErrorCodes.Hmac,
                TestHelpers.ErrorWhenReading(changedTag, TestHelpers.Password)
            );

            // Змінений байт солі
            byte[] changedSalt = TestHelpers.Copy(volumes[0]);
            changedSalt[10] ^= 0x01;
            Assert.Equal(
                ErrorCodes.Hmac,
                TestHelpers.ErrorWhenReading(changedSalt, TestHelpers.Password)
            );
        }

        // Обрізаний, короткий і чужий файл відхиляються
        [Fact]
        public void ArchiveTruncatedShortAndBadMagicAreRejected()
        {
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("a.txt", TestHelpers.Hello480()),
                TestHelpers.Password,
                TestHelpers.DefaultSettings()
            );

            // Обрізаний на 5 байтів
            var truncated = new byte[volumes[0].Length - 5];
            Array.Copy(volumes[0], truncated, truncated.Length);
            Assert.Equal(
                ErrorCodes.Length,
                TestHelpers.ErrorWhenReading(truncated, TestHelpers.Password)
            );

            // Коротший за 73 байти
            Assert.Equal(
                ErrorCodes.Short,
                TestHelpers.ErrorWhenReading(new byte[72], TestHelpers.Password)
            );

            // Інший magic
            byte[] badMagic = TestHelpers.Copy(volumes[0]);
            badMagic[0] = (byte)'X';
            Assert.Equal(
                ErrorCodes.Magic,
                TestHelpers.ErrorWhenReading(badMagic, TestHelpers.Password)
            );

            // Інша версія
            byte[] badVersion = TestHelpers.Copy(volumes[0]);
            badVersion[4] = 2;
            Assert.Equal(
                ErrorCodes.Magic,
                TestHelpers.ErrorWhenReading(badVersion, TestHelpers.Password)
            );

            // ciphertext_len не відповідає справжній довжині
            byte[] badLength = TestHelpers.Copy(volumes[0]);
            badLength[40]++;
            Assert.Equal(
                ErrorCodes.Length,
                TestHelpers.ErrorWhenReading(badLength, TestHelpers.Password)
            );
        }

        // Небезпечне ім'я відхиляється
        [Fact]
        public void PayloadUnsafeNameIsRejected()
        {
            var file = new FileRecord();
            file.Name = "xx_secret.txt";
            var payload = new Payload();
            payload.Files.Add(file);

            byte[] volume = TestHelpers.SealPayload(
                TestHelpers.WithUnsafeName(payload, "xx_secret.txt", "../secret.txt")
            );
            Assert.Equal(
                ErrorCodes.Payload,
                TestHelpers.ErrorWhenReading(volume, TestHelpers.Password)
            );
        }

        // Небезпечне ім'я не записується в архів: Payload.ToBytes дає помилку
        [Fact]
        public void PayloadWriterRefusesUnsafeName()
        {
            var file = new FileRecord();
            file.Name = "../secret.txt";
            var payload = new Payload();
            payload.Files.Add(file);

            bool refused = false;
            try
            {
                payload.ToBytes();
            }
            catch (ArgumentException)
            {
                refused = true;
            }
            Assert.True(refused);
        }

        // Ненульове поле reserved відхиляється
        [Fact]
        public void PayloadNonZeroReservedIsRejected()
        {
            byte[] payload = new Payload().ToBytes();
            payload[3] = 1; // reserved

            byte[] volume = TestHelpers.SealPayload(payload);
            Assert.Equal(
                ErrorCodes.Payload,
                TestHelpers.ErrorWhenReading(volume, TestHelpers.Password)
            );
        }

        // Неправильний CRC32 дає E_CRC
        [Fact]
        public void PayloadWrongCrcIsRejected()
        {
            byte[] data = Encoding.ASCII.GetBytes("abc");

            var file = new FileRecord();
            file.Name = "abc.txt";
            file.OriginalSize = data.Length;
            file.Crc = Crc32.Compute(data) ^ 1; // навмисно неправильна

            var block = new BlockRecord();
            block.RawLength = data.Length;
            block.Flags = BlockRecord.NoCompressionFlag;
            block.Data = data;

            var payload = new Payload();
            payload.Files.Add(file);
            payload.Blocks.Add(block);

            byte[] volume = TestHelpers.SealPayload(payload.ToBytes());
            Assert.Equal(
                ErrorCodes.Crc,
                TestHelpers.ErrorWhenReading(volume, TestHelpers.Password)
            );
        }
    }
}
