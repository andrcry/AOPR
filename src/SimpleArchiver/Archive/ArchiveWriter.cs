using System.Security.Cryptography;
using System.Text;

namespace SimpleArchiver
{
    // Створює архів: визначає тип файла, ріже файл на блоки, стискає їх LZ77 і Гаффманом,
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
            List<Payload> payloads = BuildPayloads(files, settings);

            // Крок 6. Зашифрувати і запломбувати кожен том
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

        // Готує навантаження всіх томів без шифрування: типи файлів, блоки, томи, вирівнювання.
        // Розмір кожного тому дорівнює 41 + довжина навантаження + 32
        public static List<Payload> BuildPayloads(List<ArchiveFile> files, Settings settings)
        {
            CheckFiles(files);

            FileClassifier? classifier = null;
            if (settings.ClassifierPath != null)
                classifier = FileClassifier.Load(settings.ClassifierPath);

            var records = new List<FileRecord>();
            var blocks = new List<PreparedBlock>();

            for (int fileNumber = 0; fileNumber < files.Count; fileNumber++)
            {
                byte[] data = files[fileNumber].Data;

                // Крок 1. Тип файла визначає перцептрон
                int fileType = classifier == null ? 0 : classifier.Classify(data);

                // Вже стиснуті файли (JPEG, ZIP, MP3) немає сенсу стискати ще раз
                bool compress = fileType != FileClassifier.AlreadyCompressed;

                // Крок 2. Параметри стиснення за типом файла, якщо їх увімкнено
                Settings fileSettings = settings;
                if (classifier != null && settings.UseTypeStrategy)
                {
                    TypeStrategy strategy = settings.TypeStrategies[fileType];
                    fileSettings = strategy.ApplyTo(settings);
                    compress = strategy.Compress;
                }

                // Стиснення можна вимкнути повністю: так таблиця впливу блоків міряє, що дають LZ77 і Гаффман
                if (!settings.UseCompression)
                    compress = false;

                var record = new FileRecord();
                record.Name = files[fileNumber].Name;
                record.OriginalSize = data.Length;
                record.Crc = Crc32.Compute(data);
                record.FileType = fileType;
                record.Strategy = compress ? (int)fileSettings.Parser : 0;
                records.Add(record);

                // Крок 3. Розбиття на блоки: фіксоване або ДП
                List<PreparedBlock> fileBlocks =
                    compress && fileSettings.UseDpBlockSplit
                        ? BlockSplitter.SplitDp(data, fileSettings)
                        : BlockSplitter.SplitFixed(data, fileSettings, compress);

                for (int b = 0; b < fileBlocks.Count; b++)
                {
                    fileBlocks[b].FileNumber = fileNumber;
                    fileBlocks[b].BlockNumber = b;
                    blocks.Add(fileBlocks[b]);
                }
            }

            // Крок 4. Розкласти блоки по томах (рюкзак)
            List<Payload> payloads = VolumePacker.Pack(records, blocks, settings);

            // Крок 5. Вирівняти розмір кожного тому, якщо це увімкнено
            if (VolumePadding.IsEnabled(settings))
                AddPadding(payloads, settings);

            return payloads;
        }

        // Доповнює навантаження кожного тому нулями до вирівняного розміру
        static void AddPadding(List<Payload> payloads, Settings settings)
        {
            int maxLength = settings.MaxVolumeBytes - VolumeHeader.Size - VolumeHeader.MacSize;

            foreach (Payload payload in payloads)
            {
                payload.Flags |= Payload.PaddingFlag;
                int length = payload.ToBytes().Length;
                payload.PaddingLength =
                    VolumePadding.PaddedLength(length, settings, maxLength) - length;
            }
        }

        // Кодує блок, дозволяючи стиснення
        public static PreparedBlock EncodeBlock(byte[] raw, Settings settings) =>
            EncodeBlock(raw, settings, true);

        // Кодує один блок. Пробує всі дозволені варіанти і бере найкоротший
        public static PreparedBlock EncodeBlock(byte[] raw, Settings settings, bool compress)
        {
            var block = new PreparedBlock();
            block.RawLength = raw.Length;
            block.Data = raw;
            block.Flags = BlockRecord.NoCompressionFlag;

            if (!compress)
                return block;

            // Варіант 1. LZ77 + Гаффман
            byte[] best = BlockCoder.Encode(Lz77.Parse(raw, settings.Parser, settings.MaxChain));

            // Варіант 2. Прогноз байта: стискаємо залишки замість байтів
            if (settings.UsePrediction)
            {
                best = Join(BytePredictor.NoPredictionHeader(), best);

                BytePredictor predictor = BytePredictor.Train(raw, settings.HistoryLength);
                byte[] residuals = predictor.MakeResiduals(raw);
                byte[] withPrediction = Join(
                    predictor.ToBytes(),
                    BlockCoder.Encode(Lz77.Parse(residuals, settings.Parser, settings.MaxChain))
                );

                if (withPrediction.Length < best.Length)
                    best = withPrediction;
            }

            // Варіант 3. Без стиснення, якщо інакше не коротше (NO_COMPRESSION)
            if (best.Length < raw.Length)
            {
                block.Data = best;
                block.Flags = 0;
            }

            return block;
        }

        // Склеює два масиви байтів в один
        static byte[] Join(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            Array.Copy(first, 0, result, 0, first.Length);
            Array.Copy(second, 0, result, first.Length, second.Length);
            return result;
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
        // Повтор шукаємо без урахування регістру: у Windows A.txt і a.txt - той самий файл,
        // тому такий архів там не розпакувався б повністю
        static void CheckFiles(List<ArchiveFile> files)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ArchiveFile file in files)
            {
                if (!SafeNames.IsSafe(file.Name))
                    throw new ArgumentException($"недопустиме ім'я файла: {file.Name}");
                if (Encoding.UTF8.GetByteCount(file.Name) > 65535)
                    throw new ArgumentException("ім'я файла задовге");
                if (names.Contains(file.Name))
                    throw new ArgumentException(
                        $"два файли з однаковим ім'ям (без урахування регістру): {file.Name}"
                    );

                names.Add(file.Name);
            }
        }
    }
}
