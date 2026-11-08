using System.Security.Cryptography;
using System.Text;

namespace SimpleArchiver
{
    // Створює архів з одного тому: ріже файли на блоки, стискає блоки LZ77 і Гаффманом,
    // збирає навантаження, шифрує AES і ставить пломбу HMAC
    public static class ArchiveWriter
    {
        const int maxCount = 65535; // найбільше значення полів file_count і block_count (uint16)

        // Створює архів. Сіль і nonce нові випадкові для кожного архіву
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

        // Створює архів із заданими сіллю і nonce. Тільки для тестів з контрольним прикладом
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

            var payload = new Payload();

            for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                byte[] data = files[fileIndex].Data;

                // Крок 1. Запис таблиці файлів: ім'я, розмір і CRC32 цілого файла
                var record = new FileRecord();
                record.Name = files[fileIndex].Name;
                record.OriginalSize = data.Length;
                record.Crc = Crc32.Compute(data);
                record.FileType = 0; // тип визначатиме перцептрон на чекпоінті 2
                record.Strategy = (int)settings.Parser; // спосіб розбору LZ77: 1 - жадібний, 2 - лінивий
                payload.Files.Add(record);

                // Крок 2. Ріжемо файл на блоки і кодуємо кожен. Порожній файл блоків не має
                int blockNumber = 0;
                for (int offset = 0; offset < data.Length; offset += settings.BlockSize)
                {
                    int size = Math.Min(settings.BlockSize, data.Length - offset);
                    var chunk = new byte[size];
                    Array.Copy(data, offset, chunk, 0, size);

                    BlockRecord block = EncodeBlock(chunk, settings);
                    block.FileIndex = fileIndex;
                    block.BlockNumber = blockNumber;
                    payload.Blocks.Add(block);

                    blockNumber++;
                }
            }

            if (payload.Files.Count > maxCount || payload.Blocks.Count > maxCount)
                throw new InvalidOperationException("забагато файлів або блоків для одного тому");

            // Крок 3. Ключі з пароля один раз, потім шифрування і пломба
            Keys keys = Keys.Derive(password, salt);
            byte[] volume = Volume.Seal(
                0,
                1,
                salt,
                MakeNonce(noncePrefix, 0),
                payload.ToBytes(),
                keys
            );

            var volumes = new List<byte[]>();
            volumes.Add(volume);
            return volumes;
        }

        // Кодує один блок LZ77 і Гаффманом. Якщо закодований блок не коротший за сирий, зберігає сирий (NO_COMPRESSION)
        public static BlockRecord EncodeBlock(byte[] raw, Settings settings)
        {
            byte[] coded = BlockCoder.Encode(Lz77.Parse(raw, settings.Parser, settings.MaxChain));

            var block = new BlockRecord();
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

        // Складає nonce: 8 байтів архіву і номер тому. Так nonce різний для кожного тому
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
