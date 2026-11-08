using System.Security.Cryptography;
using System.Text;

namespace SimpleArchiver
{
    // Створює архів: ріже файл на блоки, стискає їх LZ77 і Гаффманом,
    // розкладає по томах, шифрує AES і ставить пломбу HMAC
    public static class ArchiveWriter
    {
        // Сіль і nonce нові випадкові для кожного архіву
        public static List<byte[]> CreateArchive(
            List<ArchiveFile> files,
            string password,
            Settings settings
        )
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] noncePrefix = RandomNumberGenerator.GetBytes(8);
            return CreateArchive(files, password, settings, salt, noncePrefix);
        }

        // Варіант із заданими сіллю і nonce. Тільки для тестів з контрольним прикладом
        public static List<byte[]> CreateArchive(
            List<ArchiveFile> files,
            string password,
            Settings settings,
            byte[] salt,
            byte[] noncePrefix
        )
        {
            CheckFiles(files);

            if (settings.BlockSize <= 0)
                throw new ArgumentException("BlockSize має бути більшим за 0");

            var records = new List<FileRecord>();
            var blocks = new List<PreparedBlock>();

            for (int fileNumber = 0; fileNumber < files.Count; fileNumber++)
            {
                byte[] data = files[fileNumber].Data;

                // Крок 1. Запис таблиці файлів
                var record = new FileRecord();
                record.Name = files[fileNumber].Name;
                record.OriginalSize = data.Length;
                record.Crc = Crc32.Compute(data);
                record.Strategy = (int)settings.Parser;
                records.Add(record);

                // Крок 2. Ріжемо файл на блоки по BlockSize і кодуємо кожен. Порожній файл блоків не має
                int blockNumber = 0;
                for (int offset = 0; offset < data.Length; offset += settings.BlockSize)
                {
                    int size = Math.Min(settings.BlockSize, data.Length - offset);
                    var chunk = new byte[size];
                    Array.Copy(data, offset, chunk, 0, size);

                    PreparedBlock block = EncodeBlock(chunk, settings);
                    block.FileNumber = fileNumber;
                    block.BlockNumber = blockNumber;
                    blocks.Add(block);

                    blockNumber++;
                }
            }

            // Крок 3. Розкласти блоки по томах (рюкзак)
            List<Payload> payloads = VolumePacker.Pack(records, blocks, settings);

            // Крок 4. Зашифрувати і запломбувати кожен том
            Keys keys = Keys.Derive(password, salt); // один раз на весь архів

            var volumes = new List<byte[]>();
            for (int i = 0; i < payloads.Count; i++)
                volumes.Add(
                    Volume.Seal(
                        i,
                        payloads.Count,
                        salt,
                        MakeNonce(noncePrefix, i),
                        payloads[i].ToBytes(),
                        keys
                    )
                );

            return volumes;
        }

        // Кодує один блок LZ77 і Гаффманом. Якщо закодований блок не коротший за сирий, зберігає сирий (NO_COMPRESSION)
        public static PreparedBlock EncodeBlock(byte[] raw, Settings settings)
        {
            byte[] coded = BlockCoder.Encode(Lz77.Parse(raw, settings.Parser, settings.MaxChain));

            var block = new PreparedBlock();
            block.RawLength = raw.Length;

            if (coded.Length < raw.Length)
            {
                block.Data = coded;
            }
            else
            {
                block.Data = raw;
                block.Flags = BlockRecord.NoCompressionFlag;
            }

            return block;
        }

        // nonce = 8 байтів архіву + номер тому. Так nonce різний для кожного тому
        static byte[] MakeNonce(byte[] prefix, int volumeIndex)
        {
            var nonce = new byte[12];
            Array.Copy(prefix, 0, nonce, 0, 8);
            nonce[8] = (byte)(volumeIndex >> 24);
            nonce[9] = (byte)(volumeIndex >> 16);
            nonce[10] = (byte)(volumeIndex >> 8);
            nonce[11] = (byte)volumeIndex;
            return nonce;
        }

        // Перевіряє, що імена безпечні, не повторюються і не задовгі
        static void CheckFiles(List<ArchiveFile> files)
        {
            var names = new List<string>();

            foreach (ArchiveFile file in files)
            {
                if (!SafeNames.IsSafe(file.Name))
                    throw new ArgumentException($"недопустиме ім'я файла: {file.Name}");
                if (names.Contains(file.Name))
                    throw new ArgumentException($"два файли з однаковим ім'ям: {file.Name}");
                if (Encoding.UTF8.GetByteCount(file.Name) > 65535)
                    throw new ArgumentException("ім'я файла задовге");

                names.Add(file.Name);
            }
        }
    }
}
