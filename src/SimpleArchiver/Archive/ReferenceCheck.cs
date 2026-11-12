using System.Globalization;
using System.Text.Json;

namespace SimpleArchiver
{
    // Команда ref-check: перевіряє еталонні і зіпсовані архіви викладача з папки data/reference/.
    // Для кожного ref_*.json розпаковує томи архіву і звіряє з описом: кількість і розміри томів,
    // імена, розміри і CRC32 файлів. Для кожного архіву з expected_failures.json перевіряє,
    // що програма відхилила його саме з очікуваним кодом
    public static class ReferenceCheck
    {
        // Друкує результат перевірки на екран. Повертає true, якщо все пройдено
        public static bool Print(string folder)
        {
            List<string> lines = Build(folder, out int passed, out int total);
            foreach (string line in lines)
                Console.WriteLine(line);

            return passed == total;
        }

        // Перевіряє всі архіви в папці і складає рядки таблиці
        public static List<string> Build(string folder, out int passed, out int total)
        {
            if (!Directory.Exists(folder))
                throw new ArgumentException($"папки {folder} немає");

            var lines = new List<string>();
            passed = 0;
            total = 0;

            // Крок 1. Еталонні архіви: ref_01_single_file.json і томи ref_01_single_file.arc
            lines.Add("Еталонні архіви");
            lines.Add("архів\tтомів\tфайлів\tрезультат\tопис");

            string[] references = Directory.GetFiles(folder, "ref_*.json");
            Array.Sort(references, StringComparer.Ordinal);

            foreach (string jsonPath in references)
            {
                string name = Path.GetFileNameWithoutExtension(jsonPath);
                total++;

                if (new FileInfo(jsonPath).Length == 0)
                {
                    lines.Add($"{name}\t-\t-\tПОМИЛКА: порожній файл .json\t");
                    continue;
                }

                try
                {
                    using JsonDocument json = JsonDocument.Parse(File.ReadAllText(jsonPath));
                    JsonElement root = json.RootElement;
                    List<string> volumePaths = FindVolumes(folder, name);

                    string result = CheckReference(root, volumePaths);
                    if (result == "OK")
                        passed++;

                    int fileCount = 0;
                    foreach (JsonProperty file in root.GetProperty("вміст").EnumerateObject())
                        fileCount++;

                    lines.Add(
                        $"{name}\t{volumePaths.Count}\t{fileCount}\t{result}\t{root.GetProperty("опис").GetString()}"
                    );
                }
                catch (JsonException e)
                {
                    lines.Add($"{name}\t-\t-\tПОМИЛКА в .json: {e.Message}\t");
                }
                catch (KeyNotFoundException)
                {
                    lines.Add($"{name}\t-\t-\tПОМИЛКА: у .json немає потрібного поля\t");
                }
            }

            // Крок 2. Зіпсовані архіви з expected_failures.json
            lines.Add("");
            lines.Add("Зіпсовані архіви");
            lines.Add("архів\tочікується\tотримано\tрезультат\tопис");

            string expectedPath = Path.Combine(folder, "expected_failures.json");
            if (!File.Exists(expectedPath) || new FileInfo(expectedPath).Length == 0)
            {
                lines.Add("файла expected_failures.json немає або він порожній");
            }
            else
            {
                // Пароль зіпсованих архівів - пароль_сеансу з team_config.json
                string password = TeamConfig.LoadIfExists(TeamConfig.DefaultPath).Password;

                using JsonDocument json = JsonDocument.Parse(File.ReadAllText(expectedPath));
                foreach (JsonProperty item in json.RootElement.EnumerateObject())
                {
                    total++;
                    string expected = item.Value.GetProperty("очікуваний_код").GetString() ?? "";
                    string description = item.Value.GetProperty("опис").GetString() ?? "";
                    string got = ErrorWhenReading(Path.Combine(folder, item.Name), password);

                    bool ok = got == expected;
                    if (ok)
                        passed++;

                    lines.Add(
                        $"{item.Name}\t{expected}\t{got}\t{(ok ? "OK" : "ПОМИЛКА")}\t{description}"
                    );
                }
            }

            lines.Add("");
            lines.Add($"Пройдено: {passed} з {total}");
            return lines;
        }

        // Розпаковує архів у пам'ять і звіряє з описом у .json. Повертає "OK" або опис помилки
        static string CheckReference(JsonElement root, List<string> volumePaths)
        {
            // Крок 1. Кількість томів і розмір кожного тому
            int volumeCount = root.GetProperty("томів").GetInt32();
            if (volumePaths.Count != volumeCount)
                return $"ПОМИЛКА: знайдено томів {volumePaths.Count}, а в .json {volumeCount}";

            var volumes = new List<byte[]>();
            int index = 0;
            foreach (JsonElement size in root.GetProperty("розміри_томів").EnumerateArray())
            {
                byte[] volume = File.ReadAllBytes(volumePaths[index]);
                if (volume.Length != size.GetInt32())
                    return $"ПОМИЛКА: том {index} має {volume.Length} байтів, а в .json {size.GetInt32()}";

                volumes.Add(volume);
                index++;
            }

            // Крок 2. Розпакувати. Програма сама перевіряє HMAC і CRC32 кожного файла
            List<ArchiveFile> files;
            try
            {
                files = ArchiveReader.ReadArchive(
                    volumes,
                    root.GetProperty("пароль").GetString() ?? ""
                );
            }
            catch (ArchiveException e)
            {
                return $"ПОМИЛКА: {e.Code}";
            }

            // Крок 3. Звірити кожен файл з розділом "вміст": ім'я, розмір і CRC32
            JsonElement content = root.GetProperty("вміст");
            int described = 0;
            foreach (JsonProperty item in content.EnumerateObject())
            {
                described++;
                ArchiveFile? found = null;
                foreach (ArchiveFile file in files)
                {
                    if (file.Name == item.Name)
                        found = file;
                }

                if (found == null)
                    return $"ПОМИЛКА: немає файла {item.Name}";

                int size = item.Value.GetProperty("розмір").GetInt32();
                if (found.Data.Length != size)
                    return $"ПОМИЛКА: розмір {item.Name} {found.Data.Length}, а в .json {size}";

                uint crc = uint.Parse(
                    item.Value.GetProperty("crc32").GetString() ?? "",
                    NumberStyles.HexNumber
                );
                if (Crc32.Compute(found.Data) != crc)
                    return $"ПОМИЛКА: CRC32 {item.Name} не збігся з .json";
            }

            if (described != files.Count)
                return $"ПОМИЛКА: в архіві файлів {files.Count}, а в .json {described}";

            return "OK";
        }

        // Пробує прочитати зіпсований архів і повертає код відмови. Якщо архів прийнято - "прийнято"
        static string ErrorWhenReading(string path, string password)
        {
            if (!File.Exists(path))
                return "немає файла";

            var volumes = new List<byte[]>();
            volumes.Add(File.ReadAllBytes(path));

            try
            {
                ArchiveReader.ReadArchive(volumes, password);
                return "прийнято";
            }
            catch (ArchiveException e)
            {
                return e.Code;
            }
        }

        // Томи архіву: ім'я.arc або ім'я.part0.arc, ім'я.part1.arc, ...
        static List<string> FindVolumes(string folder, string name)
        {
            var volumes = new List<string>();
            string single = Path.Combine(folder, name + ".arc");

            if (File.Exists(single))
            {
                volumes.Add(single);
                return volumes;
            }

            for (int i = 0; File.Exists(Path.Combine(folder, $"{name}.part{i}.arc")); i++)
                volumes.Add(Path.Combine(folder, $"{name}.part{i}.arc"));

            return volumes;
        }
    }
}
