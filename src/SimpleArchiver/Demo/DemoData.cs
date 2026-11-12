using System.Security.Cryptography;
using System.Text;

namespace SimpleArchiver
{
    // Команда demo-data: створює замінник файлів викладача для демонстрації.
    // Структура папки така сама, як у data/: team_config.json, corpus/, train/, valid/, reference/.
    // Це НЕ файли викладача: корпус штучний, а еталонні архіви створила наша ж програма.
    // Тому вони показують, що програма працює, але не доводять сумісність з іншими командами
    public static class DemoData
    {
        public const string Password = "algo2026"; // пароль демо-архівів, як у контрольному прикладі специфікації
        const int defaultSeed = 2026; // початковий стан генератора, якщо не задано --seed

        const int corpusFilesPerGroup = 3; // файлів кожної групи в corpus/
        const int trainFilesPerGroup = 50; // 7 * 50 = 350 файлів, як у train/ викладача
        const int validFilesPerGroup = 15; // 7 * 15 = 105 файлів, як у valid/ викладача

        // Читає аргументи команди: папка і, можливо, --seed число
        public static void Run(string[] args)
        {
            int seed = defaultSeed;
            string? folder = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--seed" && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[i + 1], out seed))
                        throw new ArgumentException("після --seed потрібне ціле число");
                    i++;
                }
                else
                {
                    folder = args[i];
                }
            }

            if (folder == null)
                throw new ArgumentException(
                    "потрібно: demo-data папка або demo-data --seed число папка"
                );

            Create(folder, seed);
            Console.WriteLine($"Демо-дані створено в папці {folder}, seed = {seed}");
            Console.WriteLine("Це не файли викладача, а замінник для демонстрації");
        }

        // Створює всі демо-файли в папці
        public static void Create(string folder, int seed)
        {
            if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length > 0)
                throw new IOException($"папка {folder} не порожня, візьміть нову папку");

            Directory.CreateDirectory(folder);

            // Крок 1. Пояснення і параметри команди
            File.WriteAllText(Path.Combine(folder, "README_DEMO.md"), DemoReadme(seed));
            File.WriteAllText(Path.Combine(folder, "team_config.json"), TeamConfig());

            // Крок 2. Корпус: по папці на кожну групу, файли по 20-80 КБ
            for (int group = 0; group < DemoFiles.groupNames.Length; group++)
            {
                string groupFolder = Path.Combine(folder, "corpus", DemoFiles.groupNames[group]);
                Directory.CreateDirectory(groupFolder);

                for (int k = 0; k < corpusFilesPerGroup; k++)
                {
                    int fileSeed = seed * 1000 + group * 100 + k;
                    int size = 20000 + (k * 7919 + group * 104729) % 60000;
                    File.WriteAllBytes(
                        Path.Combine(groupFolder, DemoFiles.FileName(group, k, fileSeed)),
                        DemoFiles.Make(group, size, fileSeed)
                    );
                }
            }

            // Крок 3. Вибірки для класифікатора з labels.csv. Різні seed, щоб файли не повторювались
            MakeSamples(Path.Combine(folder, "train"), trainFilesPerGroup, seed * 1000 + 10000);
            MakeSamples(Path.Combine(folder, "valid"), validFilesPerGroup, seed * 1000 + 20000);

            // Крок 4. Еталонні архіви і зіпсовані архіви
            string reference = Path.Combine(folder, "reference");
            Directory.CreateDirectory(reference);
            MakeReferences(reference, seed);
            MakeBadArchives(reference);
        }

        // Створює папку з файлами всіх семи груп і файл labels.csv
        static void MakeSamples(string folder, int perGroup, int firstSeed)
        {
            Directory.CreateDirectory(folder);
            var labels = new StringBuilder("file,type,type_name,size\n");

            for (int group = 0; group < DemoFiles.groupNames.Length; group++)
            {
                for (int k = 0; k < perGroup; k++)
                {
                    int fileSeed = firstSeed + group * 1000 + k;
                    int size = 2000 + (k * 7919 + group * 104729) % 14000;
                    string name = DemoFiles.FileName(group, k, fileSeed);

                    byte[] data = DemoFiles.Make(group, size, fileSeed);
                    File.WriteAllBytes(Path.Combine(folder, name), data);
                    labels.Append(
                        $"{name},{group},{FileClassifier.classNames[group]},{data.Length}\n"
                    );
                }
            }

            File.WriteAllText(Path.Combine(folder, "labels.csv"), labels.ToString());
        }

        // Шість еталонних архівів, як у розділі 9 специфікації
        static void MakeReferences(string folder, int seed)
        {
            // ref_01: контрольний приклад з розділу 8 специфікації, ті самі сіль і nonce
            var hello = new StringBuilder();
            for (int i = 0; i < 40; i++)
                hello.Append("hello world ");

            byte[] salt = new byte[16];
            for (int i = 0; i < 16; i++)
                salt[i] = (byte)i;
            byte[] noncePrefix = { 1, 2, 3, 4, 5, 6, 7, 8 };

            var files1 = new List<ArchiveFile>();
            files1.Add(new ArchiveFile("hello.txt", Encoding.ASCII.GetBytes(hello.ToString())));
            List<byte[]> volumes1 = ArchiveWriter.CreateArchive(
                files1,
                Password,
                new Settings(),
                salt,
                noncePrefix
            );
            SaveReference(
                folder,
                "ref_01_single_file",
                "один файл hello.txt, контрольний приклад з розділу 8",
                files1,
                volumes1
            );

            // ref_02: файл нульового розміру
            var files2 = new List<ArchiveFile>();
            files2.Add(new ArchiveFile("empty.txt", new byte[0]));
            SaveReference(
                folder,
                "ref_02_empty_file",
                "файл нульового розміру",
                files2,
                Pack(files2, new Settings())
            );

            // ref_03: вироджений алфавіт, один символ на 5000 байтів
            var degenerate = new byte[5000];
            for (int i = 0; i < degenerate.Length; i++)
                degenerate[i] = (byte)'a';
            var files3 = new List<ArchiveFile>();
            files3.Add(new ArchiveFile("degenerate.txt", degenerate));
            SaveReference(
                folder,
                "ref_03_degenerate",
                "вироджений алфавіт: один символ на 5000 байтів",
                files3,
                Pack(files3, new Settings())
            );

            // ref_04: по одному файлу з кожної з семи груп
            var files4 = new List<ArchiveFile>();
            for (int group = 0; group < DemoFiles.groupNames.Length; group++)
                files4.Add(
                    new ArchiveFile(
                        DemoFiles.FileName(group, 0, seed + group),
                        DemoFiles.Make(group, 8000, seed + group)
                    )
                );
            SaveReference(
                folder,
                "ref_04_seven_groups",
                "по одному файлу з кожної з семи груп",
                files4,
                Pack(files4, new Settings())
            );

            // ref_05: вже стиснуті дані, блоки записуються без стиснення (NO_COMPRESSION)
            var files5 = new List<ArchiveFile>();
            files5.Add(new ArchiveFile("photo.jpg", DemoFiles.Compressed(20000, seed + 50)));
            SaveReference(
                folder,
                "ref_05_no_compression",
                "вже стиснуті дані, режим NO_COMPRESSION",
                files5,
                Pack(files5, new Settings())
            );

            // ref_06: два томи. Малий том, щоб блоки великого файла розійшлися по двох томах
            var settings6 = new Settings();
            settings6.BlockSize = 16384;
            settings6.MaxVolumeBytes = 40000;
            var files6 = new List<ArchiveFile>();
            files6.Add(new ArchiveFile("noise.bin", DemoFiles.Compressed(50000, seed + 60)));
            files6.Add(new ArchiveFile("notes.txt", DemoFiles.Text(3000, seed + 61)));
            SaveReference(
                folder,
                "ref_06_two_volumes",
                "багатотомний архів, склеювання файлів між томами",
                files6,
                Pack(files6, settings6)
            );
        }

        // Пакує файли з новими випадковими сіллю і nonce
        static List<byte[]> Pack(List<ArchiveFile> files, Settings settings) =>
            ArchiveWriter.CreateArchive(files, Password, settings);

        // Записує томи архіву і файл .json з переліком файлів, їхніми розмірами і CRC32
        static void SaveReference(
            string folder,
            string name,
            string description,
            List<ArchiveFile> files,
            List<byte[]> volumes
        )
        {
            // Крок 1. Томи: один том - ім'я.arc, кілька - ім'я.part0.arc, ім'я.part1.arc, ...
            var volumeNames = new List<string>();
            for (int i = 0; i < volumes.Count; i++)
            {
                string volumeName = volumes.Count == 1 ? $"{name}.arc" : $"{name}.part{i}.arc";
                File.WriteAllBytes(Path.Combine(folder, volumeName), volumes[i]);
                volumeNames.Add(volumeName);
            }

            // Крок 2. Опис архіву
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"description\": \"{description}\",\n");
            json.Append($"  \"password\": \"{Password}\",\n");
            json.Append("  \"volumes\": [");
            for (int i = 0; i < volumeNames.Count; i++)
                json.Append((i > 0 ? ", " : "") + $"\"{volumeNames[i]}\"");
            json.Append("],\n");
            json.Append("  \"files\": [\n");
            for (int i = 0; i < files.Count; i++)
            {
                string crc = Crc32.Compute(files[i].Data).ToString("X8");
                json.Append(
                    $"    {{\"name\": \"{files[i].Name}\", \"size\": {files[i].Data.Length}, \"crc32\": \"{crc}\"}}"
                );
                json.Append(i < files.Count - 1 ? ",\n" : "\n");
            }
            json.Append("  ]\n}\n");

            File.WriteAllText(Path.Combine(folder, name + ".json"), json.ToString());
        }

        // Шість зіпсованих архівів, як у розділі 9 специфікації, і очікувані коди відмови
        static void MakeBadArchives(string folder)
        {
            byte[] good = File.ReadAllBytes(Path.Combine(folder, "ref_01_single_file.arc"));

            // bad_01: змінено один байт шифротексту
            byte[] bad1 = (byte[])good.Clone();
            bad1[VolumeHeader.Size + 5] ^= 0x01;
            File.WriteAllBytes(Path.Combine(folder, "bad_01_tampered_byte.arc"), bad1);

            // bad_02: змінено останній байт пломби HMAC
            byte[] bad2 = (byte[])good.Clone();
            bad2[bad2.Length - 1] ^= 0x01;
            File.WriteAllBytes(Path.Combine(folder, "bad_02_tampered_tag.arc"), bad2);

            // bad_03: архів обрізано на 20 байтів
            var bad3 = new byte[good.Length - 20];
            Array.Copy(good, bad3, bad3.Length);
            File.WriteAllBytes(Path.Combine(folder, "bad_03_truncated.arc"), bad3);

            // bad_04: зіпсовано MAGIC, перша літера SARC
            byte[] bad4 = (byte[])good.Clone();
            bad4[0] = (byte)'X';
            File.WriteAllBytes(Path.Combine(folder, "bad_04_bad_magic.arc"), bad4);

            // bad_05 і bad_06 мають правильну пломбу, а помилка всередині зашифрованої частини.
            // Тому їх треба зібрати заново: зіпсувати навантаження і лише потім зашифрувати і запломбувати
            var files = new List<ArchiveFile>();
            files.Add(
                new ArchiveFile(
                    "hello.txt",
                    Encoding.ASCII.GetBytes("hello world hello world hello world")
                )
            );

            // bad_05: поле reserved не нуль
            byte[] payload5 = ArchiveWriter.BuildPayloads(files, new Settings())[0].ToBytes();
            payload5[3] = 1;
            File.WriteAllBytes(Path.Combine(folder, "bad_05_reserved.arc"), SealOne(payload5));

            // bad_06: CRC32 у таблиці файлів не збігається з даними
            Payload payload6 = ArchiveWriter.BuildPayloads(files, new Settings())[0];
            payload6.Files[0].Crc ^= 1;
            File.WriteAllBytes(
                Path.Combine(folder, "bad_06_bad_crc.arc"),
                SealOne(payload6.ToBytes())
            );

            // Очікувані коди відмови
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"bad_01_tampered_byte.arc\": \"{ErrorCodes.Hmac}\",\n");
            json.Append($"  \"bad_02_tampered_tag.arc\": \"{ErrorCodes.Hmac}\",\n");
            json.Append($"  \"bad_03_truncated.arc\": \"{ErrorCodes.Length}\",\n");
            json.Append($"  \"bad_04_bad_magic.arc\": \"{ErrorCodes.Magic}\",\n");
            json.Append($"  \"bad_05_reserved.arc\": \"{ErrorCodes.Payload}\",\n");
            json.Append($"  \"bad_06_bad_crc.arc\": \"{ErrorCodes.Crc}\"\n");
            json.Append("}\n");
            File.WriteAllText(Path.Combine(folder, "expected_failures.json"), json.ToString());
        }

        // Шифрує і пломбує одне навантаження як архів з одного тому
        static byte[] SealOne(byte[] payload)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            return Volume.Seal(0, 1, salt, nonce, payload, Keys.Derive(Password, salt));
        }

        // Параметри команди. Значення, яких ще немає від викладача, позначені як демо
        static string TeamConfig()
        {
            var settings = new Settings();
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append(
                "  \"примітка\": \"демо-значення, замінити на team_config.json від викладача\",\n"
            );
            json.Append("  \"alpha\": 1.0,\n");
            json.Append("  \"beta\": 0.0,\n");
            json.Append("  \"t_ref\": 1.0,\n");
            json.Append($"  \"max_volume_bytes\": {settings.MaxVolumeBytes},\n");
            json.Append($"  \"block_size\": {settings.BlockSize},\n");
            json.Append("  \"пріоритети_типів\": [1, 1, 1, 1, 1, 1, 1],\n");
            json.Append("  \"фіксовані_вартості_ДП\": {\"літерал\": 9, \"посилання\": 17},\n");
            json.Append("  \"F1_min\": 0.8,\n");
            json.Append("  \"протокол_навчання\": {\"ініціалізацій\": 3}\n");
            json.Append("}\n");
            return json.ToString();
        }

        // Пояснення в папці демо-даних
        static string DemoReadme(int seed)
        {
            return "# Демо-дані\n\n"
                + $"Створено командою `demo-data`, seed = {seed}. З тим самим seed виходять ті самі файли корпусу і вибірок.\n\n"
                + "Це НЕ файли викладача. Корпус штучний: сім груп файлів, створених генератором `DemoFiles`. "
                + "Еталонні архіви створила наша ж програма, тому вони показують, що програма працює, "
                + "але не доводять сумісність з архіваторами інших команд.\n\n"
                + "Пароль усіх архівів у `reference/`: `"
                + Password
                + "`. Архів `ref_01_single_file.arc` зібрано "
                + "з сіллю і nonce контрольного прикладу з розділу 8 специфікації.\n\n"
                + "Коли викладач видасть справжні файли, покладіть їх у `data/` і запускайте ті самі команди з `data/` замість цієї папки.\n";
        }
    }
}
