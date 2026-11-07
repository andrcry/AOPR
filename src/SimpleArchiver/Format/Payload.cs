using System.Text;

namespace SimpleArchiver
{
    // Навантаження тому - те, що всередині шифротексту:
    // file_count, flags, reserved, таблиця файлів, block_count, таблиця блоків, дані блоків
    public class Payload
    {
        public int Flags { get; set; } // прапорці тому
        public List<FileRecord> Files { get; set; } = new List<FileRecord>(); // таблиця файлів цього тому
        public List<BlockRecord> Blocks { get; set; } = new List<BlockRecord>(); // блоки цього тому

        // Записує навантаження в байти у порядку специфікації
        public byte[] ToBytes()
        {
            var writer = new ByteWriter();

            writer.WriteU16(Files.Count);
            writer.WriteU8(Flags);
            writer.WriteU8(0); // reserved

            foreach (FileRecord file in Files)
            {
                // Те саме правило, що й у Parse: не створюємо архів, який самі ж відхилимо
                if (!SafeNames.IsSafe(file.Name))
                    throw new ArgumentException(
                        $"небезпечне ім'я файла: {file.Name}",
                        nameof(Files)
                    );

                byte[] name = Encoding.UTF8.GetBytes(file.Name);
                writer.WriteU16(name.Length);
                writer.WriteBytes(name);
                writer.WriteU32((uint)file.OriginalSize);
                writer.WriteU32(file.Crc);
                writer.WriteU8(file.FileType);
                writer.WriteU8(file.Strategy);
            }

            writer.WriteU16(Blocks.Count);

            foreach (BlockRecord block in Blocks)
            {
                writer.WriteU16(block.FileIndex);
                writer.WriteU32((uint)block.BlockNumber);
                writer.WriteU32((uint)block.RawLength);
                writer.WriteU32((uint)block.Data.Length);
                writer.WriteU8(block.Flags);
            }

            foreach (BlockRecord block in Blocks)
                writer.WriteBytes(block.Data);

            return writer.ToArray();
        }

        // Розбирає навантаження і перевіряє кожне поле
        public static Payload Parse(byte[] data)
        {
            var reader = new ByteReader(data, ErrorCodes.Payload);
            var payload = new Payload();

            int fileCount = reader.ReadU16();
            payload.Flags = reader.ReadU8();
            int reserved = reader.ReadU8();

            if (reserved != 0)
                throw new ArchiveException(ErrorCodes.Payload, "поле reserved не дорівнює 0");
            if (payload.Flags != 0)
                throw new ArchiveException(ErrorCodes.Payload, "невідомі прапорці flags");

            // Таблиця файлів
            var strictUtf8 = new UTF8Encoding(false, true);
            var names = new List<string>();

            for (int i = 0; i < fileCount; i++)
            {
                var file = new FileRecord();
                byte[] nameBytes = reader.ReadBytes(reader.ReadU16());

                try
                {
                    file.Name = strictUtf8.GetString(nameBytes);
                }
                catch (DecoderFallbackException)
                {
                    throw new ArchiveException(
                        ErrorCodes.Payload,
                        "ім'я файла не є правильним UTF-8"
                    );
                }

                if (!SafeNames.IsSafe(file.Name))
                    throw new ArchiveException(
                        ErrorCodes.Payload,
                        $"небезпечне ім'я файла: {file.Name}"
                    );
                if (names.Contains(file.Name))
                    throw new ArchiveException(
                        ErrorCodes.Payload,
                        "ім'я файла повторюється в томі"
                    );

                names.Add(file.Name);

                file.OriginalSize = reader.ReadU32AsInt();
                file.Crc = reader.ReadU32();
                file.FileType = reader.ReadU8();
                file.Strategy = reader.ReadU8();

                if (file.FileType > 6)
                    throw new ArchiveException(ErrorCodes.Payload, "file_type більший за 6");

                payload.Files.Add(file);
            }

            // Таблиця блоків. Дані блоків ідуть після неї
            int blockCount = reader.ReadU16();
            var codedLengths = new List<int>();

            for (int i = 0; i < blockCount; i++)
            {
                var block = new BlockRecord();
                block.FileIndex = reader.ReadU16();
                block.BlockNumber = reader.ReadU32AsInt();
                block.RawLength = reader.ReadU32AsInt();
                int codedLength = reader.ReadU32AsInt();
                block.Flags = reader.ReadU8();

                if (block.FileIndex >= payload.Files.Count)
                    throw new ArchiveException(
                        ErrorCodes.Payload,
                        "file_index вказує на неіснуючий файл"
                    );
                if ((block.Flags & ~BlockRecord.NoCompressionFlag) != 0)
                    throw new ArchiveException(ErrorCodes.Payload, "невідомі біти block_flags");
                if (block.IsNotCompressed && codedLength != block.RawLength)
                    throw new ArchiveException(
                        ErrorCodes.Payload,
                        "NO_COMPRESSION, але coded_len не дорівнює raw_len"
                    );

                payload.Blocks.Add(block);
                codedLengths.Add(codedLength);
            }

            for (int i = 0; i < blockCount; i++)
                payload.Blocks[i].Data = reader.ReadBytes(codedLengths[i]);

            if (reader.Remaining != 0)
                throw new ArchiveException(ErrorCodes.Payload, "після даних блоків є зайві байти");

            return payload;
        }
    }
}
