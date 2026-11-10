using System.Diagnostics;

namespace SimpleArchiver
{
    // Таблиця для звіту: рюкзак першого тому, динамічне програмування проти жадібного алгоритму.
    // Блоки кодуються так само, як в архіваторі, потім обидва алгоритми обирають блоки для першого тому
    public static class KnapsackTable
    {
        // Друкує таблицю на екран
        public static void Print(string[] args, int start)
        {
            foreach (string line in Build(args, start))
                Console.WriteLine(line);
        }

        // Складає рядки таблиці. Аргументи: опції --volume байти і --model файл, потім файли або папки
        public static List<string> Build(string[] args, int start)
        {
            var settings = new Settings();
            FileClassifier? classifier = null;
            int index = start;

            // Крок 1. Опції: розмір тому і класифікатор типу файла
            while (index < args.Length && args[index].StartsWith("--"))
            {
                if (index + 1 >= args.Length)
                    throw new ArgumentException($"після {args[index]} потрібне значення");

                if (args[index] == "--volume")
                    settings.MaxVolumeBytes = int.Parse(args[index + 1]);
                else if (args[index] == "--model")
                    classifier = FileClassifier.Load(args[index + 1]);
                else
                    throw new ArgumentException($"невідома опція {args[index]}");

                index += 2;
            }

            List<string> paths = InputFiles.Collect(args, index);
            if (paths.Count == 0)
                throw new ArgumentException("потрібен хоча б один файл");

            // Крок 2. Кодуємо блоки всіх файлів. Вага блока - його розмір, цінність - пріоритет типу файла
            var weights = new List<int>();
            var values = new List<int>();
            int tableSize = 0;

            foreach (string path in paths)
            {
                byte[] data = File.ReadAllBytes(path);
                int fileType = classifier == null ? 0 : classifier.Classify(data);
                bool compress = fileType != FileClassifier.AlreadyCompressed;

                var record = new FileRecord();
                record.Name = Path.GetFileName(path);
                tableSize += record.SizeInTable();

                foreach (PreparedBlock block in BlockSplitter.SplitFixed(data, settings, compress))
                {
                    weights.Add(block.Weight());
                    values.Add(settings.GetPriority(fileType));
                }
            }

            // Крок 3. Місткість першого тому: таблицю файлів резервуємо до рюкзака
            int capacity = settings.MaxVolumeBytes - VolumePacker.FixedOverhead - tableSize;
            if (capacity < 0)
                throw new InvalidOperationException(
                    "таблиця файлів не вміщується в том: збільште --volume"
                );

            int[] weightArray = weights.ToArray();
            int[] valueArray = values.ToArray();

            // Крок 4. Розв'язуємо рюкзак обома алгоритмами на тих самих блоках
            Stopwatch timer = Stopwatch.StartNew();
            List<int> dp = Knapsack.SolveDp(weightArray, valueArray, capacity);
            double dpTime = timer.Elapsed.TotalMilliseconds;

            timer.Restart();
            List<int> greedy = Knapsack.SolveGreedy(weightArray, valueArray, capacity);
            double greedyTime = timer.Elapsed.TotalMilliseconds;

            long dpValue = Knapsack.TotalValue(dp, valueArray);
            long greedyValue = Knapsack.TotalValue(greedy, valueArray);
            long difference = dpValue - greedyValue;
            double percent = dpValue == 0 ? 0 : 100.0 * difference / dpValue;

            long allWeight = 0;
            foreach (int w in weightArray)
                allWeight += w;

            // Крок 5. Рядки таблиці
            var lines = new List<string>();
            lines.Add("Рюкзак першого тому, ДП проти жадібного алгоритму");
            lines.Add("алгоритм\tблоків_обрано\tвага_байтів\tцінність\tчас_мс");
            lines.Add(
                $"ДП\t{dp.Count}\t{Knapsack.TotalWeight(dp, weightArray)}\t{dpValue}\t{dpTime:F2}"
            );
            lines.Add(
                $"жадібний\t{greedy.Count}\t{Knapsack.TotalWeight(greedy, weightArray)}\t{greedyValue}\t{greedyTime:F2}"
            );
            lines.Add("");
            lines.Add($"файлів\t{paths.Count}");
            lines.Add($"блоків_всього\t{weightArray.Length}");
            lines.Add($"сума_ваг_байтів\t{allWeight}");
            lines.Add($"місткість_тому_байтів\t{capacity}");
            lines.Add($"різниця_цінності\t{difference}");
            lines.Add($"різниця_відсотків\t{percent:F2}");
            return lines;
        }
    }
}
