namespace SimpleArchiver
{
    // Таблиця для звіту: ентропія самих байтів і ентропія залишків прогнозу по групах файлів.
    // Якщо ентропія залишків нижча, прогноз робить дані більш стисливими
    public static class EntropyTable
    {
        const double minGain = 0.01; // різниця, меншу за 0.01 біта на байт, вважаємо випадковою

        // Друкує таблицю на екран
        public static void Print(string[] args, int start)
        {
            foreach (string line in Build(args, start))
                Console.WriteLine(line);
        }

        // Складає рядки таблиці. Кожен аргумент - файл або папка. Папка - одна група, усі її файли рахуються разом.
        // Якщо в папці є labels.csv (як у data/corpus від викладача), папка ділиться на групи за типом файла
        public static List<string> Build(string[] args, int start)
        {
            var settings = new Settings();
            var lines = new List<string>();
            lines.Add("Ентропія байтів і залишків прогнозу, біт на байт");
            lines.Add("група\tфайлів\tбайтів\tентропія_байтів\tентропія_залишків\tпрогноз_допоміг");

            // Крок 1. Складаємо групи: назва групи і її файли
            var groupNames = new List<string>();
            var groupFiles = new List<List<string>>();

            for (int i = start; i < args.Length; i++)
            {
                List<string> files = InputFiles.Collect(new string[] { args[i] }, 0);

                if (Directory.Exists(args[i]) && File.Exists(Path.Combine(args[i], "labels.csv")))
                {
                    // Одна група на кожен тип 0-6
                    for (int type = 0; type < FileClassifier.ClassCount; type++)
                    {
                        var ofType = new List<string>();
                        foreach (string file in files)
                        {
                            if (Dataset.TypeOf(file) == type)
                                ofType.Add(file);
                        }

                        if (ofType.Count > 0)
                        {
                            groupNames.Add(FileClassifier.classNames[type]);
                            groupFiles.Add(ofType);
                        }
                    }
                }
                else
                {
                    groupNames.Add(GroupName(args[i]));
                    groupFiles.Add(files);
                }
            }

            // Крок 2. Для кожної групи ентропія байтів і ентропія залишків прогнозу
            for (int g = 0; g < groupNames.Count; g++)
                lines.Add(GroupRow(groupNames[g], groupFiles[g], settings));

            return lines;
        }

        // Рядок таблиці для однієї групи файлів
        static string GroupRow(string name, List<string> files, Settings settings)
        {
            var byteCounts = new long[256];
            var residualCounts = new long[256];
            long total = 0;

            foreach (string file in files)
            {
                byte[] data = File.ReadAllBytes(file);
                total += data.Length;
                AddCounts(data, byteCounts);

                // Прогноз рахується по блоках, так само як в архіваторі
                for (int offset = 0; offset < data.Length; offset += settings.BlockSize)
                {
                    int size = Math.Min(settings.BlockSize, data.Length - offset);
                    var chunk = new byte[size];
                    Array.Copy(data, offset, chunk, 0, size);

                    BytePredictor predictor = BytePredictor.Train(chunk, settings.HistoryLength);
                    AddCounts(predictor.MakeResiduals(chunk), residualCounts);
                }
            }

            double byteEntropy = Entropy(byteCounts, total);
            double residualEntropy = Entropy(residualCounts, total);
            string helped = residualEntropy < byteEntropy - minGain ? "так" : "ні";

            return $"{name}\t{files.Count}\t{total}\t{byteEntropy:F3}\t{residualEntropy:F3}\t{helped}";
        }

        // Назва групи: ім'я папки або файла без шляху
        static string GroupName(string path)
        {
            return Path.GetFileName(path.TrimEnd('/', '\\'));
        }

        // Додає до лічильників, скільки разів трапився кожен байт
        static void AddCounts(byte[] data, long[] counts)
        {
            foreach (byte b in data)
                counts[b]++;
        }

        // Ентропія Шеннона за лічильниками: H = -sum p * log2(p)
        static double Entropy(long[] counts, long total)
        {
            if (total == 0)
                return 0;

            double h = 0;
            foreach (long count in counts)
            {
                if (count == 0)
                    continue;

                double p = (double)count / total;
                h -= p * Math.Log2(p);
            }
            return h;
        }
    }
}
