using System.Globalization;
using System.Security.Cryptography;

namespace SimpleArchiver
{
    // Команда crypto-order: обов'язковий експеримент «зашифрувати перед стисненням».
    //
    // Порівнюємо два порядки операцій на тих самих файлах і з тими самими налаштуваннями:
    //   1) правильний: стиснути -> зашифрувати (так працює наш архіватор);
    //   2) зворотний: зашифрувати -> стиснути.
    // Шифрування AES-CTR не змінює довжину даних, тому розмір архіву в обох випадках дорівнює
    // розміру навантаження томів після стиснення + 41 байт заголовка + 32 байти HMAC на кожен том.
    // Різниця тільки в тому, ЩО потрапляє на вхід стиснення: відкриті дані чи шифротекст.
    // Додатково рахуємо ентропію байтів до і після шифрування - вона пояснює результат
    public static class CryptoOrderTable
    {
        const string ExperimentPassword = "crypto-order-experiment"; // пароль лише для експерименту

        // Друкує таблицю на екран
        public static void Print(string[] args, int start)
        {
            foreach (string line in Build(args, start))
                Console.WriteLine(line);
        }

        // Складає рядки таблиці. Аргументи: файли або папки. Групи беруться з labels.csv у папці файла
        public static List<string> Build(string[] args, int start)
        {
            List<string> paths = InputFiles.Collect(args, start);
            if (paths.Count == 0)
                throw new ArgumentException("потрібен хоча б один файл");

            var settings = new Settings();

            // Крок 1. Ключ шифрування з пароля, як в архіваторі (PBKDF2, перші 32 байти - K_enc)
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            Keys keys = Keys.Derive(ExperimentPassword, salt);

            // Крок 2. Для кожного файла - відкриті дані і шифротекст тієї самої довжини
            var plain = new List<ArchiveFile>();
            var encrypted = new List<ArchiveFile>();
            var types = new List<int>();

            for (int i = 0; i < paths.Count; i++)
            {
                byte[] data = File.ReadAllBytes(paths[i]);
                string name = Path.GetFileName(paths[i]);
                byte[] nonce = RandomNumberGenerator.GetBytes(12); // свій nonce для кожного файла

                plain.Add(new ArchiveFile(name, data));
                encrypted.Add(new ArchiveFile(name, Crypto.AesCtr(keys.EncryptionKey, nonce, data)));
                types.Add(Dataset.TypeOf(paths[i]));
            }

            var lines = new List<string>();
            lines.Add("Порядок операцій: стиснути -> зашифрувати проти зашифрувати -> стиснути");
            lines.Add(
                $"Розбір {settings.Parser}, блоки по {settings.BlockSize} байтів, том до {settings.MaxVolumeBytes} байтів"
            );
            lines.Add(
                "група\tфайлів\tB_orig\tстиснути_потім_шифрувати\tкоеф_1\tшифрувати_потім_стиснути\tкоеф_2\t"
                    + "ентропія_відкритих_біт\tентропія_шифротексту_біт"
            );

            // Крок 3. Окремий рядок для кожної групи корпусу
            for (int type = 0; type < FileClassifier.ClassCount; type++)
            {
                var groupPlain = new List<ArchiveFile>();
                var groupEncrypted = new List<ArchiveFile>();
                for (int i = 0; i < plain.Count; i++)
                {
                    if (types[i] != type)
                        continue;
                    groupPlain.Add(plain[i]);
                    groupEncrypted.Add(encrypted[i]);
                }

                if (groupPlain.Count > 0)
                    lines.Add(Row(FileClassifier.classNames[type], groupPlain, groupEncrypted, settings));
            }

            // Крок 4. Файли без мітки (labels.csv немає) і підсумок по всьому корпусу
            var unlabeledPlain = new List<ArchiveFile>();
            var unlabeledEncrypted = new List<ArchiveFile>();
            for (int i = 0; i < plain.Count; i++)
            {
                if (types[i] >= 0)
                    continue;
                unlabeledPlain.Add(plain[i]);
                unlabeledEncrypted.Add(encrypted[i]);
            }
            if (unlabeledPlain.Count > 0)
                lines.Add(Row("без мітки", unlabeledPlain, unlabeledEncrypted, settings));

            lines.Add(Row("УСЬОГО", plain, encrypted, settings));
            lines.Add("");
            lines.Add(
                "Висновок: шифротекст AES-CTR має ентропію майже 8 біт на байт і не містить повторів, "
                    + "тому LZ77 і Гаффман не знаходять надлишковості, і блоки зберігаються без стиснення (NO_COMPRESSION). "
                    + "Стискати треба до шифрування."
            );
            return lines;
        }

        // Один рядок таблиці: розміри архіву в обох порядках і ентропії
        static string Row(
            string name,
            List<ArchiveFile> plain,
            List<ArchiveFile> encrypted,
            Settings settings
        )
        {
            long original = 0;
            foreach (ArchiveFile file in plain)
                original += file.Data.Length;

            long compressThenEncrypt = ArchiveSize(plain, settings);
            long encryptThenCompress = ArchiveSize(encrypted, settings);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}\t{1}\t{2}\t{3}\t{4:F3}\t{5}\t{6:F3}\t{7:F3}\t{8:F3}",
                name,
                plain.Count,
                original,
                compressThenEncrypt,
                Ratio(original, compressThenEncrypt),
                encryptThenCompress,
                Ratio(original, encryptThenCompress),
                Entropy(plain),
                Entropy(encrypted)
            );
        }

        // Розмір архіву: навантаження всіх томів після стиснення + заголовок і HMAC кожного тому.
        // AES-CTR довжину не змінює, тому шифрування на розмір не впливає
        public static long ArchiveSize(List<ArchiveFile> files, Settings settings)
        {
            List<Payload> payloads = ArchiveWriter.BuildPayloads(files, settings);
            long size = 0;
            foreach (Payload payload in payloads)
                size += VolumeHeader.Size + payload.ToBytes().Length + VolumeHeader.MacSize;
            return size;
        }

        // Ентропія Шеннона по байтах усіх файлів разом, біт на байт
        public static double Entropy(List<ArchiveFile> files)
        {
            var counts = new long[256];
            long total = 0;
            foreach (ArchiveFile file in files)
            {
                foreach (byte b in file.Data)
                    counts[b]++;
                total += file.Data.Length;
            }

            if (total == 0)
                return 0;

            double entropy = 0;
            foreach (long count in counts)
            {
                if (count == 0)
                    continue;
                double p = (double)count / total;
                entropy -= p * Math.Log2(p);
            }
            return entropy;
        }

        static double Ratio(long original, long archive) =>
            archive > 0 ? (double)original / archive : 0;
    }
}
