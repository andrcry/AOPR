using System.Text;

namespace SimpleArchiver.Tests
{
    // Структури формату SARC без шифрування: заголовок тому і навантаження
    public class FormatTests
    {
        // Складає том з правильним заголовком: 41 байт заголовка, 10 байтів шифротексту і 32 байти пломби
        static byte[] MakeVolume()
        {
            var header = new VolumeHeader();
            header.VolumeIndex = 2;
            header.VolumeCount = 3;
            for (int i = 0; i < 16; i++)
                header.Salt[i] = (byte)(i + 1);
            for (int i = 0; i < 12; i++)
                header.Nonce[i] = (byte)(100 + i);
            header.CiphertextLength = 10;

            var volume = new byte[VolumeHeader.Size + 10 + VolumeHeader.MacSize];
            Array.Copy(header.ToBytes(), volume, VolumeHeader.Size);
            return volume;
        }

        // Пробує розібрати заголовок і повертає код помилки або "OK"
        static string ErrorWhenParsingHeader(byte[] volume)
        {
            try
            {
                VolumeHeader.Parse(volume);
            }
            catch (ArchiveException e)
            {
                return e.Code;
            }

            return "OK";
        }

        // Пробує розібрати навантаження і повертає код помилки або "OK"
        static string ErrorWhenParsingPayload(byte[] data)
        {
            try
            {
                Payload.Parse(data);
            }
            catch (ArchiveException e)
            {
                return e.Code;
            }

            return "OK";
        }

        // Навантаження з одним файлом і одним сирим блоком
        static Payload MakePayload()
        {
            byte[] data = Encoding.ASCII.GetBytes("abc");

            var file = new FileRecord();
            file.Name = "a.txt";
            file.OriginalSize = data.Length;
            file.Crc = Crc32.Compute(data);
            file.Strategy = 1;

            var block = new BlockRecord();
            block.RawLength = data.Length;
            block.Flags = BlockRecord.NoCompressionFlag;
            block.Data = data;

            var payload = new Payload();
            payload.Files.Add(file);
            payload.Blocks.Add(block);
            return payload;
        }

        // Заголовок записується в 41 байт і читається назад без змін
        [Fact]
        public void VolumeHeaderRoundTrip()
        {
            byte[] volume = MakeVolume();
            VolumeHeader parsed = VolumeHeader.Parse(volume);

            Assert.Equal(2, parsed.VolumeIndex);
            Assert.Equal(3, parsed.VolumeCount);
            Assert.Equal(1, parsed.Salt[0]);
            Assert.Equal(16, parsed.Salt[15]);
            Assert.Equal(100, parsed.Nonce[0]);
            Assert.Equal(10, parsed.CiphertextLength);
        }

        // Короткий, чужий і обрізаний том відхиляються
        [Fact]
        public void VolumeHeaderRejectsBadVolumes()
        {
            Assert.Equal(ErrorCodes.Short, ErrorWhenParsingHeader(new byte[72]));

            byte[] badMagic = MakeVolume();
            badMagic[0] = (byte)'X';
            Assert.Equal(ErrorCodes.Magic, ErrorWhenParsingHeader(badMagic));

            byte[] badVersion = MakeVolume();
            badVersion[4] = 2;
            Assert.Equal(ErrorCodes.Magic, ErrorWhenParsingHeader(badVersion));

            byte[] full = MakeVolume();
            var cut = new byte[full.Length - 1];
            Array.Copy(full, cut, cut.Length);
            Assert.Equal(ErrorCodes.Length, ErrorWhenParsingHeader(cut));
        }

        // Навантаження записується і читається назад без змін
        [Fact]
        public void PayloadRoundTrip()
        {
            Payload parsed = Payload.Parse(MakePayload().ToBytes());

            Assert.Equal("a.txt", parsed.Files[0].Name);
            Assert.Equal(3, parsed.Files[0].OriginalSize);
            Assert.Equal(Crc32.Compute(Encoding.ASCII.GetBytes("abc")), parsed.Files[0].Crc);
            Assert.True(parsed.Blocks[0].IsNotCompressed);
            Assert.Equal(Encoding.ASCII.GetBytes("abc"), parsed.Blocks[0].Data);
        }

        // Неправильні поля навантаження дають E_PAYLOAD
        [Fact]
        public void PayloadRejectsBadFields()
        {
            byte[] reserved = MakePayload().ToBytes();
            reserved[3] = 1;
            Assert.Equal(ErrorCodes.Payload, ErrorWhenParsingPayload(reserved));

            Payload unsafeName = MakePayload();
            unsafeName.Files[0].Name = "xx_secret.txt";
            Assert.Equal(
                ErrorCodes.Payload,
                ErrorWhenParsingPayload(
                    TestHelpers.WithUnsafeName(unsafeName, "xx_secret.txt", "../secret.txt")
                )
            );

            Payload wrongLength = MakePayload();
            wrongLength.Blocks[0].RawLength = 5; // без стиснення coded_len має дорівнювати raw_len
            Assert.Equal(ErrorCodes.Payload, ErrorWhenParsingPayload(wrongLength.ToBytes()));

            byte[] good = MakePayload().ToBytes();
            var extra = new byte[good.Length + 1];
            Array.Copy(good, extra, good.Length);
            Assert.Equal(ErrorCodes.Payload, ErrorWhenParsingPayload(extra));
        }
    }
}
