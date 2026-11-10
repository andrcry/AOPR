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

        // Складає рядки таблиці. Кожен аргумент - файл або папка. Папка - одна група, усі її файли рахуються разом
        public static List<string> Build(string[] args, int start)
        {
            var settings = new Settings();
            var lines = new List<string>();
            lines.Add("Ентропія байтів і залишків прогнозу, біт на байт");
            lines.Add("група\tфайлів\tбайтів\tентропія_байтів\tентропія_залишків\tпрогноз_допоміг");

            for (int i = start; i < args.Length; i++)
            {
                List<string> files = InputFiles.Collect(new string[] { args[i] }, 0);

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

                        BytePredictor predictor = BytePredictor.Train(
                            chunk,
                            settings.HistoryLength
                        );
                        AddCounts(predictor.MakeResiduals(chunk), residualCounts);
                    }
                }

                double byteEntropy = Entropy(byteCounts, total);
                double residualEntropy = Entropy(residualCounts, total);
                string helped = residualEntropy < byteEntropy - minGain ? "так" : "ні";

                lines.Add(
                    $"{GroupName(args[i])}\t{files.Count}\t{total}\t{byteEntropy:F3}\t{residualEntropy:F3}\t{helped}"
                );
            }

            return lines;
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
