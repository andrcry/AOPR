namespace SimpleArchiver
{
    // Завантажує вибірку train/ або valid/.
    // Очікується файл labels.csv з рядками "ім'я_файла,тип" (тип - число 0-6 або назва групи).
    // Якщо labels.csv немає, шукаються підпапки 0, 1, ..., 6 з файлами відповідного типу
    public static class Dataset
    {
        // Можливі назви груп у labels.csv. Якщо у викладача інші - дописати сюди
        static Dictionary<string, int> labelNames = new Dictionary<string, int>
        {
            ["text"] = 0,
            ["natural_text"] = 0,
            ["природний текст"] = 0,
            ["текст"] = 0,
            ["code"] = 1,
            ["source"] = 1,
            ["source_code"] = 1,
            ["вихідний код"] = 1,
            ["код"] = 1,
            ["structured"] = 2,
            ["data"] = 2,
            ["структуровані дані"] = 2,
            ["raster"] = 3,
            ["image"] = 3,
            ["растр"] = 3,
            ["растр без стиснення"] = 3,
            ["executable"] = 4,
            ["binary"] = 4,
            ["виконуваний файл"] = 4,
            ["compressed"] = 5,
            ["вже стиснуте"] = 5,
            ["synthetic"] = 6,
            ["artificial"] = 6,
            ["штучні дані"] = 6,
        };

        // Завантажує вибірку з папки: з labels.csv або з підпапок 0-6
        public static List<Sample> Load(string folder)
        {
            string labelsPath = Path.Combine(folder, "labels.csv");

            if (File.Exists(labelsPath))
                return LoadFromCsv(folder, labelsPath);

            return LoadFromSubfolders(folder);
        }

        // Читає файли і їхні типи за рядками labels.csv
        static List<Sample> LoadFromCsv(string folder, string labelsPath)
        {
            var samples = new List<Sample>();

            foreach (string line in File.ReadAllLines(labelsPath))
            {
                string[] parts = line.Split(',', ';');
                if (parts.Length < 2)
                    continue;

                string fileName = parts[0].Trim();
                int label = ParseLabel(parts[parts.Length - 1]);
                if (label < 0)
                    continue; // рядок-заголовок або невідома мітка

                string path = Path.Combine(folder, fileName);
                if (!File.Exists(path))
                {
                    Console.WriteLine($"Файл {fileName} з labels.csv не знайдено, пропускаємо.");
                    continue;
                }

                samples.Add(
                    new Sample(fileName, FileFeatures.Compute(File.ReadAllBytes(path)), label)
                );
            }

            return samples;
        }

        // Читає файли з підпапок, назва підпапки - номер типу
        static List<Sample> LoadFromSubfolders(string folder)
        {
            var samples = new List<Sample>();

            for (int label = 0; label < FileClassifier.ClassCount; label++)
            {
                string subfolder = Path.Combine(folder, label.ToString());
                if (!Directory.Exists(subfolder))
                    continue;

                foreach (string path in Directory.GetFiles(subfolder))
                    samples.Add(
                        new Sample(
                            Path.GetFileName(path),
                            FileFeatures.Compute(File.ReadAllBytes(path)),
                            label
                        )
                    );
            }

            return samples;
        }

        // Повертає номер типу або -1, якщо мітку не вдалося розпізнати
        static int ParseLabel(string text)
        {
            string clean = text.Trim().Trim('"').ToLowerInvariant();

            if (
                int.TryParse(clean, out int number)
                && number >= 0
                && number < FileClassifier.ClassCount
            )
                return number;

            if (labelNames.TryGetValue(clean, out int label))
                return label;

            return -1;
        }
    }
}
