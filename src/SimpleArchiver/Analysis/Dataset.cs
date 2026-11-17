namespace SimpleArchiver
{
    // Завантажує вибірку train/ або valid/.
    // Викладач дає labels.csv з рядками "file,type,type_name,size", наприклад "f0101.pgm,3,растр без стиснення,16192".
    // Тип - число 0-6 у колонці type. Якщо labels.csv немає, шукаються підпапки 0, 1, ..., 6 з файлами відповідного типу
    public static class Dataset
    {
        // Завантажує вибірку з папки: з labels.csv або з підпапок 0-6
        public static List<Sample> Load(string folder)
        {
            string labelsPath = Path.Combine(folder, "labels.csv");

            if (File.Exists(labelsPath))
                return LoadFromCsv(folder, labelsPath);

            return LoadFromSubfolders(folder);
        }

        // Читає labels.csv і повертає тип кожного файла: ім'я файла - тип 0-6
        public static Dictionary<string, int> ReadLabels(string labelsPath)
        {
            var labels = new Dictionary<string, int>();
            string[] lines = File.ReadAllLines(labelsPath);
            if (lines.Length == 0)
                return labels;

            // У першому рядку назви колонок. Шукаємо, де ім'я файла і де тип.
            // Якщо назв колонок немає, ім'я файла - перша колонка, тип - друга
            string[] header = lines[0].Split(',');
            int fileColumn = 0;
            int typeColumn = 1;
            int firstLine = 0;
            for (int c = 0; c < header.Length; c++)
            {
                string name = header[c].Trim();
                if (name == "file")
                {
                    fileColumn = c;
                    firstLine = 1;
                }
                if (name == "type")
                {
                    typeColumn = c;
                    firstLine = 1;
                }
            }

            // Решта рядків: ім'я файла і число 0-6
            for (int i = firstLine; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(',');
                if (parts.Length <= Math.Max(fileColumn, typeColumn))
                    continue;

                if (
                    !int.TryParse(parts[typeColumn].Trim(), out int type)
                    || type < 0
                    || type >= FileClassifier.ClassCount
                )
                    throw new InvalidOperationException(
                        $"неправильний тип у {labelsPath}, рядок {i + 1}"
                    );

                labels[parts[fileColumn].Trim()] = type;
            }

            return labels;
        }

        // Вже прочитані labels.csv: папка - типи її файлів. Щоб не читати той самий файл для кожного файла папки
        static Dictionary<string, Dictionary<string, int>> labelsByFolder =
            new Dictionary<string, Dictionary<string, int>>();

        // Тип файла з labels.csv, що лежить у тій самій папці. Якщо labels.csv немає або файла в ньому немає - мінус 1
        public static int TypeOf(string filePath)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? "";
            Dictionary<string, int> labels;

            // lock - щоб два потоки (наприклад, паралельні тести) не змінювали словник одночасно
            lock (labelsByFolder)
            {
                if (!labelsByFolder.ContainsKey(folder))
                {
                    string labelsPath = Path.Combine(folder, "labels.csv");
                    if (File.Exists(labelsPath))
                        labelsByFolder[folder] = ReadLabels(labelsPath);
                    else
                        labelsByFolder[folder] = new Dictionary<string, int>();
                }

                labels = labelsByFolder[folder];
            }

            string name = Path.GetFileName(filePath);
            if (labels.ContainsKey(name))
                return labels[name];

            return -1;
        }

        // Читає файли і їхні типи за рядками labels.csv
        static List<Sample> LoadFromCsv(string folder, string labelsPath)
        {
            var samples = new List<Sample>();
            Dictionary<string, int> labels = ReadLabels(labelsPath);

            // Імена беремо за абеткою, щоб порядок не залежав від порядку рядків у файлі
            var names = new List<string>(labels.Keys);
            names.Sort(StringComparer.Ordinal);

            foreach (string fileName in names)
            {
                string path = Path.Combine(folder, fileName);
                if (!File.Exists(path))
                {
                    Console.WriteLine($"Файл {fileName} з labels.csv не знайдено, пропускаємо.");
                    continue;
                }

                samples.Add(
                    new Sample(
                        fileName,
                        FileFeatures.Compute(File.ReadAllBytes(path)),
                        labels[fileName]
                    )
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
    }
}
