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
    }
}
