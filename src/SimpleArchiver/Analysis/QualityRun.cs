namespace SimpleArchiver
{
    // Команда q: базове Q на корпусі (розділ 4 завдання).
    // Програма пакує і розпаковує весь корпус, перевіряє кожен файл побайтово і рахує:
    //   S = B_orig / (B_orig + B_arc)   наскільки добре стискає
    //   T = t_ref / (t_ref + t)         наскільки швидко працює, t - час пакування і розпакування
    //   Q = alpha * S + beta * T        alpha, beta і t_ref беруться з team_config.json
    // Якщо хоча б один файл не відновлено побайтово, Q = 0.
    // Кожен прогін дописується в лог-файл experiments/corpus_run.log
    public static class QualityRun
    {
        public const string DefaultLog = "experiments/corpus_run.log"; // лог прогонів корпусу
        public const string DefaultCorpus = "data/corpus"; // корпус від викладача

        // Друкує результат на екран
        public static void Print(string[] args, int start)
        {
            foreach (string line in Build(args, start))
                Console.WriteLine(line);
        }

        // Складає рядки результату. Аргументи: опції, потім файли або папки (без них - data/corpus)
        public static List<string> Build(string[] args, int start)
        {
            var settings = new Settings();
            string logPath = DefaultLog;
            int index = start;

            // Опції, такі самі, як у pack: --parser, --split dp, --predict, --model, --strategy, і ще --log
            while (index < args.Length && args[index].StartsWith("--"))
            {
                string option = args[index];

                if (option == "--predict")
                {
                    settings.UsePrediction = true;
                    index++;
                    continue;
                }

                if (option == "--strategy")
                {
                    settings.UseTypeStrategy = true;
                    index++;
                    continue;
                }

                if (index + 1 >= args.Length)
                    throw new ArgumentException($"після {option} потрібне значення");

                string value = args[index + 1];
                index += 2;

                if (option == "--parser")
                    settings.Parser = ParseParser(value);
                else if (option == "--split" && value == "dp")
                    settings.UseDpBlockSplit = true;
                else if (option == "--model")
                    settings.ClassifierPath = value;
                else if (option == "--log")
                    logPath = value;
                else
                    throw new ArgumentException($"невідома опція {option} {value}");
            }

            if (settings.UseTypeStrategy && settings.ClassifierPath == null)
                throw new ArgumentException("опція --strategy працює лише разом з --model");

            // Корпус і параметри метрики з team_config.json
            List<ArchiveFile> corpus = LoadCorpus(args, index);
            TeamConfig config = TeamConfig.LoadIfExists(TeamConfig.DefaultPath);

            // Пакування, розпакування і перевірка кожного файла
            QEvaluation result = Measure(corpus, settings, config, "базове Q", logPath);

            // Рядки результату: параметри, значення S, T і Q і розрахунок за формулою
            var lines = new List<string>();
            lines.Add(
                $"Метрика Q на корпусі: {corpus.Count} файлів, {result.OriginalBytes} байтів"
            );
            lines.Add(
                $"Параметри з team_config.json: alpha = {config.Alpha}, beta = {config.Beta}, t_ref = {config.TRef} с"
            );
            lines.Add($"Налаштування: {Describe(settings)}");
            lines.Add("B_orig\tB_arc\tt_с\tS\tT\tQ\tвідновлено");
            lines.Add(Row(result));
            lines.Add(
                $"Q = {config.Alpha} * {result.S:F4} + {config.Beta} * {result.T:F4} = {result.Q:F4}"
            );

            if (!result.RestoredExact)
                lines.Add($"Не всі файли відновлено, тому Q = 0. Причина: {result.ErrorMessage}");

            lines.Add($"Запис про прогін додано в {logPath}");
            return lines;
        }

        // Пакує і розпаковує корпус, рахує S, T і Q і дописує рядок у лог-файл
        public static QEvaluation Measure(
            List<ArchiveFile> corpus,
            Settings settings,
            TeamConfig config,
            string name,
            string logPath
        )
        {
            // Папку для лог-файла створюємо, якщо її ще немає
            string? folder = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            return QualityMetric.Evaluate(corpus, settings, config, name, logPath);
        }

        // Читає файли корпусу. Якщо файлів у аргументах немає - береться data/corpus
        public static List<ArchiveFile> LoadCorpus(string[] args, int start)
        {
            List<string> paths;
            if (start < args.Length)
                paths = InputFiles.Collect(args, start);
            else
                paths = InputFiles.Collect(new string[] { DefaultCorpus }, 0);

            if (paths.Count == 0)
                throw new ArgumentException("корпус порожній");

            var corpus = new List<ArchiveFile>();
            foreach (string path in paths)
                corpus.Add(new ArchiveFile(Path.GetFileName(path), File.ReadAllBytes(path)));

            return corpus;
        }

        // Рядок таблиці з одним вимірюванням
        public static string Row(QEvaluation result)
        {
            string restored = result.RestoredExact ? "так" : "НІ";
            return $"{result.OriginalBytes}\t{result.ArchiveBytes}\t{result.ElapsedSeconds:F3}\t{result.S:F4}\t{result.T:F4}\t{result.Q:F4}\t{restored}";
        }

        // Короткий опис налаштувань для звіту
        public static string Describe(Settings settings)
        {
            string text =
                $"розбір {settings.Parser}, блок {settings.BlockSize}, том {settings.MaxVolumeBytes}";
            if (settings.UseDpBlockSplit)
                text += ", ДП-розбиття";
            if (settings.UsePrediction)
                text += ", прогноз байта";
            if (settings.ClassifierPath != null)
                text += ", класифікатор";
            if (settings.UseTypeStrategy)
                text += ", стратегії за типом";
            return text;
        }

        // Перетворює текст опції --parser на спосіб розбору
        static ParserKind ParseParser(string value)
        {
            if (value == "greedy")
                return ParserKind.Greedy;
            if (value == "lazy")
                return ParserKind.Lazy;
            if (value == "dp")
                return ParserKind.Dp;

            throw new ArgumentException("після --parser потрібно greedy, lazy або dp");
        }
    }
}
