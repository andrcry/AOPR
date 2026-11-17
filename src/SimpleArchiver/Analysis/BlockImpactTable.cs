namespace SimpleArchiver
{
    // Команда q-table: таблиця впливу кожного блока на Q (розділ 12 завдання).
    // Спершу Q рахується для повної системи, де ввімкнено всі реалізовані блоки.
    // Потім по черзі вимикається один блок (або замінюється простішим) і Q рахується знову.
    // Різниця Q показує, що дає цей блок. Якщо блок не дає виграшу, у звіті треба пояснити чому
    public static class BlockImpactTable
    {
        public const string DefaultTable = "experiments/q_table.tsv"; // таблиця для звіту

        const int rowCount = 9; // повна система і 8 змін

        // Назви рядків таблиці
        static string[] rowNames =
        {
            "повна система",
            "без прогнозу байта",
            "без ДП-розбиття на блоки",
            "лінивий розбір замість ДП",
            "жадібний розбір замість ДП",
            "без класифікатора типу файла",
            "жадібний рюкзак замість ДП",
            "без стиснення LZ77 і Гаффмана",
            "зі стратегіями за типом файла",
        };

        // Друкує таблицю на екран і записує її у файл experiments/q_table.tsv
        public static void Print(string[] args, int start)
        {
            string? modelPath = null;
            int index = start;

            // Опція --model: готовий класифікатор. Без неї класифікатор навчається на data/train і data/valid
            if (index + 1 < args.Length && args[index] == "--model")
            {
                modelPath = args[index + 1];
                index += 2;
            }

            List<ArchiveFile> corpus = QualityRun.LoadCorpus(args, index);
            TeamConfig config = TeamConfig.LoadIfExists(TeamConfig.DefaultPath);

            string? tempModel = null;
            if (modelPath == null)
            {
                tempModel = TrainTempClassifier(config);
                modelPath = tempModel;
            }

            try
            {
                // Будуємо таблицю. Кожен прогін також дописується в experiments/corpus_run.log
                List<string> lines = Build(corpus, config, modelPath, QualityRun.DefaultLog);
                foreach (string line in lines)
                    Console.WriteLine(line);

                Directory.CreateDirectory(Path.GetDirectoryName(DefaultTable) ?? ".");
                File.WriteAllLines(DefaultTable, lines);
                Console.WriteLine($"Таблицю записано у {DefaultTable}");
            }
            finally
            {
                if (tempModel != null)
                    File.Delete(tempModel);
            }
        }

        // Складає рядки таблиці. modelPath - файл класифікатора, null - рядки з класифікатором пропускаються
        public static List<string> Build(
            List<ArchiveFile> corpus,
            TeamConfig config,
            string? modelPath,
            string logPath
        )
        {
            var lines = new List<string>();
            lines.Add(
                $"Вплив кожного блока на Q: корпус {corpus.Count} файлів, alpha = {config.Alpha}, beta = {config.Beta}, t_ref = {config.TRef} с"
            );
            lines.Add("конфігурація\tB_arc\tt_с\tS\tT\tQ\tзміна_Q\tвисновок");

            QEvaluation? full = null;

            for (int row = 0; row < rowCount; row++)
            {
                // Без класифікатора рядки 5 і 8 не мають сенсу: класифікатор і так вимкнено
                bool needsClassifier = row == 5 || row == 8;
                if (modelPath == null && needsClassifier)
                {
                    lines.Add($"{rowNames[row]}\t-\t-\t-\t-\t-\t-\tпропущено: немає класифікатора");
                    continue;
                }

                // Налаштування рядка і вимір Q
                Settings settings = MakeSettings(row, modelPath);
                QEvaluation result = QualityRun.Measure(
                    corpus,
                    settings,
                    config,
                    rowNames[row],
                    logPath
                );
                if (row == 0)
                    full = result;

                // Зміна Q відносно повної системи і висновок
                double change = full == null ? 0 : result.Q - full.Q;
                change = Math.Round(change, 4);
                if (change == 0)
                    change = 0; // щоб замість "-0,0000" друкувалося "0,0000"
                string verdict = Verdict(row, change, result);

                lines.Add(
                    $"{rowNames[row]}\t{result.ArchiveBytes}\t{result.ElapsedSeconds:F3}\t{result.S:F4}\t{result.T:F4}\t{result.Q:F4}\t{change:F4}\t{verdict}"
                );
            }

            return lines;
        }

        // Налаштування для рядка таблиці. Повна система - усі блоки ввімкнено, далі змінюється по одному
        static Settings MakeSettings(int row, string? modelPath)
        {
            // Повна система: ДП-розбір, ДП-розбиття, прогноз байта, класифікатор, ДП-рюкзак
            var settings = new Settings();
            settings.Parser = ParserKind.Dp;
            settings.UseDpBlockSplit = true;
            settings.UsePrediction = true;
            settings.ClassifierPath = modelPath;
            settings.UseDpKnapsack = true;

            // Для рядка змінюємо рівно один блок
            if (row == 1)
                settings.UsePrediction = false;
            else if (row == 2)
                settings.UseDpBlockSplit = false;
            else if (row == 3)
                settings.Parser = ParserKind.Lazy;
            else if (row == 4)
                settings.Parser = ParserKind.Greedy;
            else if (row == 5)
                settings.ClassifierPath = null;
            else if (row == 6)
                settings.UseDpKnapsack = false;
            else if (row == 7)
                settings.UseCompression = false;
            else if (row == 8)
                settings.UseTypeStrategy = true;

            return settings;
        }

        // Висновок для рядка. Рядки 1-7 вимикають блок: якщо Q впало, блок корисний.
        // Рядок 8 додає блок: якщо Q зросло, стратегії корисні
        static string Verdict(int row, double change, QEvaluation result)
        {
            const double noise = 0.002; // зміну, меншу за це, вважаємо шумом: час між прогонами коливається

            if (!result.RestoredExact)
                return "файли не відновлено, Q = 0";
            if (row == 0)
                return "основа для порівняння";

            if (row == 8)
            {
                if (change > noise)
                    return "стратегії покращують Q";
                if (change < -noise)
                    return "стратегії погіршують Q";
                return "стратегії майже не змінюють Q";
            }

            if (change < -noise)
                return "блок покращує Q";
            if (change > noise)
                return "без блока Q вище: блок не дає виграшу";
            return "блок майже не змінює Q";
        }

        // Навчає класифікатор на data/train і data/valid і зберігає його в тимчасовий файл.
        // Якщо вибірок немає, повертає null, і рядки з класифікатором буде пропущено
        static string? TrainTempClassifier(TeamConfig config)
        {
            if (!Directory.Exists("data/train") || !Directory.Exists("data/valid"))
                return null;

            Console.WriteLine("Навчаємо класифікатор на data/train і data/valid");
            List<Sample> train = Dataset.Load("data/train");
            List<Sample> valid = Dataset.Load("data/valid");
            if (train.Count == 0 || valid.Count == 0)
                return null;

            var seeds = new int[config.Initializations];
            for (int i = 0; i < seeds.Length; i++)
                seeds[i] = i + 1;

            FileClassifier classifier = FileClassifier.TrainBest(
                train,
                valid,
                seeds,
                300,
                0.1,
                false
            );
            string path = Path.Combine(
                Path.GetTempPath(),
                "sarc-q-model-" + Guid.NewGuid().ToString("N") + ".txt"
            );
            classifier.Save(path);
            return path;
        }
    }
}
