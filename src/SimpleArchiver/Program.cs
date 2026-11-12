using System.Diagnostics;
using System.Numerics;

namespace SimpleArchiver
{
    // Команди програми:
    // pack - створити архів. Спочатку опції, потім ім'я архіву і файли.
    //   опція --parser greedy, lazy або dp - спосіб розбору LZ77, за замовчуванням lazy
    //   опція --split dp - ДП-розбиття на блоки
    //   опція --predict - прогноз наступного байта
    //   опція --model файл - класифікатор типу файла
    //   опція --strategy - параметри стиснення за типом файла (разом з --model)
    //   опція --pad байти - вирівняти розмір тому до кратного цьому числу
    //   опція --pad-pow2 - вирівняти розмір тому до степеня двійки
    //   опція --dh мій.private чужий.public - пароль зі спільного ключа Діффі - Геллмана
    // unpack - розпакувати: папка, потім томи. Перед папкою можна дати --dh мій.private чужий.public
    // measure - таблиці для звіту по файлах
    // train - навчити класифікатор: папка train, папка valid, файл моделі
    // classify - визначити тип файлів і параметри стиснення для них: файл моделі, потім файли
    // knapsack-table - таблиця рюкзака першого тому: опції --volume і --model, потім файли або папки
    // entropy-table - таблиця ентропії залишків прогнозу: файли або папки, кожна папка - одна група
    // leak-table - таблиця витоку метаданих: опція --pad байти, потім файли або папки
    // dh-keys - створити ключі Діффі - Геллмана: ім'я, вийдуть файли ім'я.private і ім'я.public
    // check - контрольні точки всіх алгоритмів
    // Пароль береться зі змінної SARC_PASSWORD або запитується
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
                string command = args[0];

                if (command == "pack" && args.Length >= 3)
                    Pack(args);
                else if (command == "unpack" && args.Length >= 3)
                    Unpack(args);
                else if (command == "measure" && args.Length >= 2)
                    Measure(args);
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
            Console.WriteLine("pack опції архів.arc файли");
            Console.WriteLine(
                "опції pack: --parser greedy, --parser lazy, --parser dp, --split dp, --predict, --model файл, --strategy"
            );
            Console.WriteLine("опції pack для захисту: --pad байти, --pad-pow2");
            Console.WriteLine("unpack папка томи");
            Console.WriteLine("measure файли");
            Console.WriteLine("train папка_train папка_valid файл_моделі");
            Console.WriteLine("classify файл_моделі файли");
            Console.WriteLine("knapsack-table опції файли_або_папки");
            Console.WriteLine("опції knapsack-table: --volume байти, --model файл");
            Console.WriteLine("entropy-table файли_або_папки");
            Console.WriteLine("leak-table опції файли_або_папки");
            Console.WriteLine("опції leak-table: --pad байти");
            Console.WriteLine("dh-keys ім'я");
            Console.WriteLine("опція pack і unpack: --dh мій.private чужий.public замість пароля");
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
            for (int i = index + 1; i < args.Length; i++)
                files.Add(new ArchiveFile(Path.GetFileName(args[i]), File.ReadAllBytes(args[i])));

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

            FileClassifier classifier = FileClassifier.TrainBest(
                train,
                valid,
                new[] { 1, 2, 3 },
                300,
                0.1,
                true
            );

            int[,] confusion = Metrics.Confusion(classifier.Net, valid);
            Console.WriteLine(
                $"Найкраща модель: macro-F1 на valid = {Metrics.MacroF1(confusion):F4}"
            );
            Metrics.PrintConfusion(confusion);

            classifier.Save(args[3]);
            Console.WriteLine($"Ваги збережено у {args[3]}");
        }

        // Показує тип, який перцептрон визначив для кожного файла, і параметри стиснення для цього типу
        static void Classify(string[] args)
        {
            FileClassifier classifier = FileClassifier.Load(args[1]);
            TypeStrategy[] strategies = new Settings().TypeStrategies;

            for (int i = 2; i < args.Length; i++)
            {
                int type = classifier.Classify(File.ReadAllBytes(args[i]));
                Console.WriteLine(
                    $"{Path.GetFileName(args[i])}: тип {type}, {FileClassifier.classNames[type]}, {strategies[type].Describe()}"
                );
            }
        }

        // Таблиці для звіту: розбори LZ77 і розбиття на блоки, з прогнозом і без
        static void Measure(string[] args)
        {
            Console.WriteLine("Розбір LZ77, блоки по 64 КБ");
            Console.WriteLine(
                "файл\tрозмір\tрозбір\tрезультат\tчас_мс\tлітерали\tпосилання\tвартість_бітів"
            );

            for (int i = 1; i < args.Length; i++)
            {
                byte[] data = File.ReadAllBytes(args[i]);
                string name = Path.GetFileName(args[i]);

                Console.WriteLine(
                    $"{name}\t{data.Length}\tжадібний\t{MeasureParser(data, ParserKind.Greedy)}"
                );
                Console.WriteLine(
                    $"{name}\t{data.Length}\tлінивий\t{MeasureParser(data, ParserKind.Lazy)}"
                );
                Console.WriteLine(
                    $"{name}\t{data.Length}\tДП\t{MeasureParser(data, ParserKind.Dp)}"
                );
            }

            Console.WriteLine();
            Console.WriteLine(
                "Розбиття на блоки і прогноз, розмір блоків разом із записами таблиці блоків"
            );
            Console.WriteLine(
                "файл\tфіксоване\tДП-розбиття\tфіксоване+прогноз\tДП-розбиття+прогноз"
            );

            for (int i = 1; i < args.Length; i++)
            {
                byte[] data = File.ReadAllBytes(args[i]);
                string fixedSize = MeasureSplit(data, false, false);
                string dpSize = MeasureSplit(data, true, false);
                string fixedPredicted = MeasureSplit(data, false, true);
                string dpPredicted = MeasureSplit(data, true, true);

                Console.WriteLine(
                    $"{Path.GetFileName(args[i])}\t{fixedSize}\t{dpSize}\t{fixedPredicted}\t{dpPredicted}"
                );
            }
        }

        // Розбирає файл по блоках і повертає рядок таблиці
        static string MeasureParser(byte[] data, ParserKind kind)
        {
            var settings = new Settings();
            settings.Parser = kind;

            long size = 0;
            long literals = 0;
            long matches = 0;
            long fixedCost = 0;
            Stopwatch timer = Stopwatch.StartNew();

            for (int offset = 0; offset < data.Length; offset += settings.BlockSize)
            {
                int length = Math.Min(settings.BlockSize, data.Length - offset);
                var chunk = new byte[length];
                Array.Copy(data, offset, chunk, 0, length);

                List<Token> tokens = Lz77.Parse(chunk, kind, settings.MaxChain);
                fixedCost += Lz77.FixedCost(tokens);

                foreach (Token token in tokens)
                {
                    if (token.IsMatch)
                        matches++;
                    else
                        literals++;
                }

                // Якщо стиснення не вигідне, блок займає raw_len (NO_COMPRESSION)
                size += Math.Min(BlockCoder.Encode(tokens).Length, chunk.Length);
            }

            return $"{size}\t{timer.ElapsedMilliseconds}\t{literals}\t{matches}\t{fixedCost}";
        }

        // Рахує розмір блоків для фіксованого або ДП-розбиття, з прогнозом або без
        static string MeasureSplit(byte[] data, bool dpSplit, bool prediction)
        {
            var settings = new Settings();
            settings.UsePrediction = prediction;

            List<PreparedBlock> blocks = dpSplit
                ? BlockSplitter.SplitDp(data, settings)
                : BlockSplitter.SplitFixed(data, settings, true);

            return BlockSplitter.TotalSize(blocks).ToString();
        }
    }
}
