using System.Numerics;

namespace SimpleArchiver
{
    // Команди програми:
    // pack - створити архів. Спочатку опції, потім ім'я архіву і файли або папки.
    //   опція --parser greedy, lazy або dp - спосіб розбору LZ77, за замовчуванням lazy
    //   опція --split dp - ДП-розбиття на блоки
    //   опція --predict - прогноз наступного байта
    //   опція --model файл - класифікатор типу файла
    //   опція --strategy - параметри стиснення за типом файла (разом з --model)
    //   опція --pad байти - вирівняти розмір тому до кратного цьому числу
    //   опція --pad-pow2 - вирівняти розмір тому до степеня двійки
    //   опція --dh мій.private чужий.public - пароль зі спільного ключа Діффі - Геллмана
    // unpack - розпакувати: папка, потім томи. Перед папкою можна дати --dh мій.private чужий.public
    // measure - таблиці розборів LZ77 і розбиття на блоки: файли або папки. Групи файлів беруться з labels.csv
    // train - навчити класифікатор: папка train, папка valid, файл моделі
    // classify - визначити тип файлів і параметри стиснення для них: файл моделі, потім файли або папки
    // knapsack-table - таблиця рюкзака першого тому: опції --volume і --model, потім файли або папки
    // entropy-table - таблиця ентропії залишків прогнозу: файли або папки, кожна папка - одна група
    // leak-table - таблиця витоку метаданих: опція --pad байти, потім файли або папки
    // dh-keys - створити ключі Діффі - Геллмана: ім'я, вийдуть файли ім'я.private і ім'я.public
    // demo-data - створити замінник файлів викладача для демонстрації: опція --seed число, потім папка
    // ref-check - перевірити еталонні і зіпсовані архіви: папка reference
    // team-config - показати параметри з data/team_config.json або з файла, переданого після команди
    // ga - оптимізація параметрів генетичним алгоритмом (опція --advanced додає повну хромосому і локальний пошук)
    // pareto - багатокритеріальна оптимізація за (S, T) і побудова фронту Парето (NSGA-II)
    // q - базове Q на корпусі: опції як у pack і --log файл, потім файли або папки (без них - data/corpus)
    // q-table - таблиця впливу кожного блока на Q: опція --model файл, потім файли або папки (без них - data/corpus)
    // check - контрольні точки всіх алгоритмів
    // Пароль береться зі змінної SARC_PASSWORD або запитується.
    // На старті програма читає data/team_config.json: розмір блока, розмір тому і пріоритети типів
    static class Program
    {
        // Читає команду і запускає потрібну дію
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (args.Length == 0)
            {
                PrintHelp();
                return 1;
            }

            try
            {
                // Параметри команди від викладача стають стандартними для всіх команд
                TeamConfig.LoadIfExists(TeamConfig.DefaultPath).Apply();

                string command = args[0];

                if (command == "pack" && args.Length >= 3)
                    Pack(args);
                else if (command == "unpack" && args.Length >= 3)
                    Unpack(args);
                else if (command == "measure" && args.Length >= 2)
                    MeasureTable.Print(args, 1);
                else if (command == "train" && args.Length == 4)
                    Train(args);
                else if (command == "classify" && args.Length >= 3)
                    Classify(args);
                else if (command == "knapsack-table" && args.Length >= 2)
                    KnapsackTable.Print(args, 1);
                else if (command == "entropy-table" && args.Length >= 2)
                    EntropyTable.Print(args, 1);
                else if (command == "leak-table" && args.Length >= 2)
                    LeakTable.Print(args, 1);
                else if (command == "dh-keys" && args.Length == 2)
                    MakeDhKeys(args[1]);
                else if (command == "demo-data" && args.Length >= 2)
                    DemoData.Run(args);
                else if (command == "ref-check" && args.Length == 2)
                    return ReferenceCheck.Print(args[1]) ? 0 : 1;
                else if (command == "team-config" && args.Length <= 2)
                    PrintTeamConfig(args);
                else if (command == "ga")
                    RunGa(args);
                else if (command == "pareto")
                    RunPareto(args);
                else if (command == "q")
                    QualityRun.Print(args, 1);
                else if (command == "q-table")
                    BlockImpactTable.Print(args, 1);
                else if (command == "check")
                    return Checks.RunAll() ? 0 : 1;
                else
                {
                    PrintHelp();
                    return 1;
                }

                return 0;
            }
            catch (ArchiveException e)
            {
                Console.WriteLine($"Архів відхилено. {e.Message}");
                return 2;
            }
            catch (IOException e)
            {
                Console.WriteLine($"Помилка файлу: {e.Message}");
                return 1;
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine($"Немає доступу: {e.Message}");
                return 1;
            }
            catch (ArgumentException e)
            {
                Console.WriteLine($"Неправильні вхідні дані: {e.Message}");
                return 1;
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine($"Помилка: {e.Message}");
                return 1;
            }
        }

        // Друкує, як користуватися програмою
        static void PrintHelp()
        {
            Console.WriteLine("Використання:");
            Console.WriteLine("pack опції архів.arc файли_або_папки");
            Console.WriteLine(
                "опції pack: --parser greedy, --parser lazy, --parser dp, --split dp, --predict, --model файл, --strategy"
            );
            Console.WriteLine("опції pack для захисту: --pad байти, --pad-pow2");
            Console.WriteLine("unpack папка томи");
            Console.WriteLine("measure файли_або_папки");
            Console.WriteLine("train папка_train папка_valid файл_моделі");
            Console.WriteLine("classify файл_моделі файли_або_папки");
            Console.WriteLine("knapsack-table опції файли_або_папки");
            Console.WriteLine("опції knapsack-table: --volume байти, --model файл");
            Console.WriteLine("entropy-table файли_або_папки");
            Console.WriteLine("leak-table опції файли_або_папки");
            Console.WriteLine("опції leak-table: --pad байти");
            Console.WriteLine("dh-keys ім'я");
            Console.WriteLine("опція pack і unpack: --dh мій.private чужий.public замість пароля");
            Console.WriteLine("demo-data опції папка");
            Console.WriteLine("опції demo-data: --seed число");
            Console.WriteLine("ref-check папка_reference");
            Console.WriteLine("team-config або team-config файл");
            Console.WriteLine("ga [опції] [файли_або_папки]");
            Console.WriteLine(
                "опції ga: --advanced, --crossover single|uniform, --pop N, --gen N, --seed N, --out файл, --model файл"
            );
            Console.WriteLine("pareto [опції] [файли_або_папки]");
            Console.WriteLine(
                "опції pareto: --pop N, --gen N, --seed N, --out файл, --model файл, --crossover single|uniform"
            );
            Console.WriteLine("q опції файли_або_папки");
            Console.WriteLine(
                "опції q: --parser greedy|lazy|dp, --split dp, --predict, --model файл, --strategy, --log файл"
            );
            Console.WriteLine("q-table --model файл файли_або_папки");
            Console.WriteLine("check");
        }

        // Бере пароль зі змінної середовища або питає з клавіатури
        static string GetPassword()
        {
            string? password = Environment.GetEnvironmentVariable("SARC_PASSWORD");
            if (!string.IsNullOrEmpty(password))
                return password;

            Console.Write("Пароль: ");
            return Console.ReadLine() ?? "";
        }

        // Пакує файли. Один том - архів.arc, кілька - архів.part0.arc, архів.part1.arc, ...
        static void Pack(string[] args)
        {
            var settings = new Settings();
            string? password = null;
            int index = 1;

            // Опції на початку: --parser, --split, --predict, --model, --strategy, --pad, --pad-pow2, --dh
            while (index < args.Length && args[index].StartsWith("--"))
            {
                string option = args[index];

                if (option == "--dh")
                {
                    if (index + 2 >= args.Length)
                        throw new ArgumentException("після --dh потрібні два файли ключів");

                    password = DhPassword(args[index + 1], args[index + 2]);
                    index += 3;
                    continue;
                }

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

                if (option == "--pad-pow2")
                {
                    settings.PadToPowerOfTwo = true;
                    index++;
                    continue;
                }

                if (index + 1 >= args.Length)
                    throw new ArgumentException($"після {option} потрібне значення");

                string value = args[index + 1];
                index += 2;

                if (option == "--parser" && value == "greedy")
                    settings.Parser = ParserKind.Greedy;
                else if (option == "--parser" && value == "lazy")
                    settings.Parser = ParserKind.Lazy;
                else if (option == "--parser" && value == "dp")
                    settings.Parser = ParserKind.Dp;
                else if (option == "--split" && value == "dp")
                    settings.UseDpBlockSplit = true;
                else if (option == "--model")
                    settings.ClassifierPath = value;
                else if (option == "--pad")
                    settings.PadUnit = ParsePositive(value, option);
                else
                    throw new ArgumentException($"невідома опція {option} {value}");
            }

            if (args.Length - index < 2)
                throw new ArgumentException("потрібні ім'я архіву і хоча б один файл");

            if (settings.UseTypeStrategy && settings.ClassifierPath == null)
                throw new ArgumentException("опція --strategy працює лише разом з --model");

            string archivePath = args[index];
            var files = new List<ArchiveFile>();
            foreach (string path in InputFiles.Collect(args, index + 1))
                files.Add(new ArchiveFile(Path.GetFileName(path), File.ReadAllBytes(path)));

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                files,
                password ?? GetPassword(),
                settings
            );

            if (volumes.Count == 1)
            {
                File.WriteAllBytes(archivePath, volumes[0]);
                Console.WriteLine($"Готово: {archivePath}, {volumes[0].Length} байтів");
                return;
            }

            string baseName = Path.ChangeExtension(archivePath, null);
            for (int i = 0; i < volumes.Count; i++)
            {
                string path = $"{baseName}.part{i}.arc";
                File.WriteAllBytes(path, volumes[i]);
                Console.WriteLine($"Том {i}: {path}, {volumes[i].Length} байтів");
            }
        }

        // Перетворює текст опції на додатне ціле число
        static int ParsePositive(string value, string option)
        {
            if (!int.TryParse(value, out int number) || number <= 0)
                throw new ArgumentException($"після {option} потрібне додатне ціле число");

            return number;
        }

        // Розпаковує томи в папку. Перед папкою можна дати --dh мій.private чужий.public замість пароля
        static void Unpack(string[] args)
        {
            int index = 1;
            string? password = null;

            if (args[index] == "--dh")
            {
                if (args.Length < 6)
                    throw new ArgumentException(
                        "потрібно: unpack --dh мій.private чужий.public папка томи"
                    );

                password = DhPassword(args[2], args[3]);
                index = 4;
            }

            var volumes = new List<byte[]>();
            for (int i = index + 1; i < args.Length; i++)
                volumes.Add(File.ReadAllBytes(args[i]));

            List<ArchiveFile> files = ArchiveReader.ReadArchive(volumes, password ?? GetPassword());
            Extractor.SaveFiles(files, args[index]);
            Console.WriteLine($"Розпаковано файлів: {files.Count}");
        }

        // Пароль архіву зі спільного ключа Діффі - Геллмана: мій секретний ключ і відкритий ключ колеги
        static string DhPassword(string privatePath, string publicPath)
        {
            BigInteger privateKey = DiffieHellman.LoadKey(privatePath);
            BigInteger otherPublicKey = DiffieHellman.LoadKey(publicPath);
            return DiffieHellman.SharedPassword(privateKey, otherPublicKey);
        }

        // Створює пару ключів Діффі - Геллмана і записує їх у файли ім'я.private і ім'я.public
        static void MakeDhKeys(string name)
        {
            BigInteger privateKey = DiffieHellman.GeneratePrivateKey();
            DiffieHellman.SaveKey(name + ".private", privateKey);
            DiffieHellman.SaveKey(name + ".public", DiffieHellman.PublicKey(privateKey));

            Console.WriteLine($"Секретний ключ: {name}.private, його нікому не передавайте");
            Console.WriteLine($"Відкритий ключ: {name}.public, його надішліть колезі");
        }

        // Навчає класифікатор: 3 ініціалізації, вибір за macro-F1 на valid
        static void Train(string[] args)
        {
            Console.WriteLine("Рахуємо ознаки файлів");
            List<Sample> train = Dataset.Load(args[1]);
            List<Sample> valid = Dataset.Load(args[2]);
            Console.WriteLine($"train: {train.Count} файлів, valid: {valid.Count} файлів");

            if (train.Count == 0 || valid.Count == 0)
                throw new InvalidOperationException("вибірка порожня: перевірте labels.csv");

            // Протокол навчання з team_config.json: стільки ініціалізацій, скільки задав викладач
            TeamConfig config = TeamConfig.LoadIfExists(TeamConfig.DefaultPath);
            var seeds = new int[config.Initializations];
            for (int i = 0; i < seeds.Length; i++)
                seeds[i] = i + 1;

            FileClassifier classifier = FileClassifier.TrainBest(
                train,
                valid,
                seeds,
                300,
                0.1,
                true
            );

            int[,] confusion = Metrics.Confusion(classifier.Net, valid);
            double f1 = Metrics.MacroF1(confusion);
            Console.WriteLine($"Найкраща модель: macro-F1 на valid = {f1:F4}");
            Metrics.PrintConfusion(confusion);

            // Порівняння з порогом і базовими лініями з team_config.json
            Console.WriteLine(
                $"Поріг F1_min = {config.F1Min}: {(f1 >= config.F1Min ? "пройдено" : "НЕ пройдено")}"
            );
            Console.WriteLine(
                $"Базові лінії: один клас {config.BaselineMajority}, випадковий вибір {config.BaselineRandom}"
            );

            classifier.Save(args[3]);
            Console.WriteLine($"Ваги збережено у {args[3]}");
        }

        // Друкує параметри з team_config.json і перевіряє, що програма їх використовує
        static void PrintTeamConfig(string[] args)
        {
            string path = args.Length == 2 ? args[1] : TeamConfig.DefaultPath;
            if (!File.Exists(path))
                throw new FileNotFoundException($"файла {path} немає");

            foreach (string line in TeamConfig.Load(path).Describe())
                Console.WriteLine(line);

            var settings = new Settings();
            Console.WriteLine(
                $"Програма зараз бере: блок {settings.BlockSize}, том {settings.MaxVolumeBytes}"
            );
        }

        // Показує тип, який перцептрон визначив для кожного файла, і параметри стиснення для цього типу
        static void Classify(string[] args)
        {
            FileClassifier classifier = FileClassifier.Load(args[1]);
            TypeStrategy[] strategies = new Settings().TypeStrategies;

            foreach (string path in InputFiles.Collect(args, 2))
            {
                int type = classifier.Classify(File.ReadAllBytes(path));
                Console.WriteLine(
                    $"{Path.GetFileName(path)}: тип {type}, {FileClassifier.classNames[type]}, {strategies[type].Describe()}"
                );
            }
        }

        // Оптимізує параметри архіватора генетичним алгоритмом і порівнює з випадковим (та локальним у --advanced) пошуком
        static void RunGa(string[] args)
        {
            TeamConfig config = TeamConfig.LoadIfExists(TeamConfig.DefaultPath);
            var options = new GaRunOptions { Seed = config.Seed };
            bool crossoverExplicit = false;
            int index = 1;

            while (index < args.Length && args[index].StartsWith("--"))
            {
                string option = args[index];
                if (option == "--advanced")
                {
                    options.Advanced = true;
                    options.AdaptiveMutation = true;
                    index++;
                    continue;
                }

                if (index + 1 >= args.Length)
                    throw new ArgumentException($"після {option} потрібне значення");

                string value = args[index + 1];
                index += 2;

                if (option == "--pop")
                    options.PopulationSize = ParsePositive(value, option);
                else if (option == "--gen")
                    options.Generations = ParsePositive(value, option);
                else if (option == "--seed")
                {
                    if (!int.TryParse(value, out int seed))
                        throw new ArgumentException($"після {option} потрібне ціле число");
                    options.Seed = seed;
                }
                else if (option == "--out")
                    options.LogPath = value;
                else if (option == "--model")
                    options.ClassifierPath = value;
                else if (option == "--crossover")
                {
                    options.Crossover = ParseCrossover(value);
                    crossoverExplicit = true;
                }
                else
                    throw new ArgumentException($"невідома опція {option}");
            }

            if (options.Advanced && !crossoverExplicit)
                options.Crossover = CrossoverType.Uniform;

            string[] inputPaths = ResolveCorpusPaths(args, index);
            List<ArchiveFile> corpus = GeneticOptimizer.LoadCorpus(inputPaths);
            long totalBytes = 0;
            foreach (ArchiveFile f in corpus)
                totalBytes += f.Data.Length;

            Console.WriteLine(
                $"Корпус: {corpus.Count} файлів, {totalBytes} байтів (labels.csv виключено)"
            );

            string? tempModelPath = null;
            if (options.Advanced && options.ClassifierPath == null)
                options.ClassifierPath = PrepareTempClassifierIfAvailable(out tempModelPath);

            try
            {
                GaComparisonReport report = GeneticOptimizer.CompareWithRandomSearch(
                    corpus,
                    config,
                    options
                );

                Console.WriteLine("Покоління ГА:");
                foreach (GaGenerationRecord row in report.Genetic.History)
                {
                    Console.WriteLine(
                        $"  gen={row.Generation} evals={row.Evaluations} mut={row.MutationRate:F2} best_Q={row.BestQ:F4} avg_Q={row.AverageQ:F4} S={row.BestS:F4} T={row.BestT:F4} [{row.BestParams}]"
                    );
                }

                Console.WriteLine();
                Console.Write(report.TableText);
                Console.WriteLine($"Журнал поколінь збережено у {report.GenerationsLogPath}");
            }
            finally
            {
                if (tempModelPath != null && File.Exists(tempModelPath))
                    File.Delete(tempModelPath);
            }
        }

        // Багатокритеріальна оптимізація за (S, T) і побудова фронту Парето (NSGA-II)
        static void RunPareto(string[] args)
        {
            TeamConfig config = TeamConfig.LoadIfExists(TeamConfig.DefaultPath);
            var options = new ParetoRunOptions { Seed = config.Seed };
            int index = 1;

            while (index < args.Length && args[index].StartsWith("--"))
            {
                string option = args[index];
                if (index + 1 >= args.Length)
                    throw new ArgumentException($"після {option} потрібне значення");

                string value = args[index + 1];
                index += 2;

                if (option == "--pop")
                    options.PopulationSize = ParsePositive(value, option);
                else if (option == "--gen")
                    options.Generations = ParsePositive(value, option);
                else if (option == "--seed")
                {
                    if (!int.TryParse(value, out int seed))
                        throw new ArgumentException($"після {option} потрібне ціле число");
                    options.Seed = seed;
                }
                else if (option == "--out")
                    options.OutputPath = value;
                else if (option == "--model")
                    options.ClassifierPath = value;
                else if (option == "--crossover")
                    options.Crossover = ParseCrossover(value);
                else
                    throw new ArgumentException($"невідома опція {option}");
            }

            string[] inputPaths = ResolveCorpusPaths(args, index);
            List<ArchiveFile> corpus = GeneticOptimizer.LoadCorpus(inputPaths);
            long totalBytes = 0;
            foreach (ArchiveFile f in corpus)
                totalBytes += f.Data.Length;

            Console.WriteLine(
                $"Корпус: {corpus.Count} файлів, {totalBytes} байтів (labels.csv виключено)"
            );

            string? tempModelPath = null;
            if (options.ClassifierPath == null)
                options.ClassifierPath = PrepareTempClassifierIfAvailable(out tempModelPath);

            try
            {
                ParetoResult result = ParetoOptimizer.Optimize(corpus, config, options);
                Console.WriteLine();
                Console.Write(result.MarkdownTable);
                Console.WriteLine($"Фронт Парето збережено у {result.OutputPath}");
            }
            finally
            {
                if (tempModelPath != null && File.Exists(tempModelPath))
                    File.Delete(tempModelPath);
            }
        }

        static CrossoverType ParseCrossover(string value)
        {
            if (string.Equals(value, "single", StringComparison.OrdinalIgnoreCase))
                return CrossoverType.SinglePoint;
            if (string.Equals(value, "uniform", StringComparison.OrdinalIgnoreCase))
                return CrossoverType.Uniform;

            throw new ArgumentException(
                $"невідомий кросовер {value}, очікується single або uniform"
            );
        }

        static string[] ResolveCorpusPaths(string[] args, int index)
        {
            if (index < args.Length)
                return args[index..];

            if (Directory.Exists("data/corpus"))
                return new[] { "data/corpus" };

            throw new ArgumentException("потрібен хоча б один файл або папка корпусу");
        }

        static string? PrepareTempClassifierIfAvailable(out string? tempModelPath)
        {
            tempModelPath = null;
            if (File.Exists("experiments/classifier.model"))
                return "experiments/classifier.model";

            if (Directory.Exists("data/train") && Directory.Exists("data/valid"))
            {
                Console.WriteLine("Навчаємо класифікатор для стратегій за типами файлів...");
                List<Sample> train = Dataset.Load("data/train");
                List<Sample> valid = Dataset.Load("data/valid");
                if (train.Count > 0 && valid.Count > 0)
                {
                    FileClassifier classifier = FileClassifier.TrainBest(
                        train,
                        valid,
                        new[] { 2 },
                        150,
                        0.1,
                        false
                    );
                    tempModelPath = Path.Combine(
                        Path.GetTempPath(),
                        "sarc-ga-model-" + Guid.NewGuid().ToString("N") + ".txt"
                    );
                    classifier.Save(tempModelPath);
                    return tempModelPath;
                }
            }

            return null;
        }
    }
}
