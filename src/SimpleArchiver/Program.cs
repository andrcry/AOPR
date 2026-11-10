using System.Diagnostics;

namespace SimpleArchiver
{
    // Команди програми:
    // pack - створити архів. Спочатку опції, потім ім'я архіву і файли.
    //   опція --parser greedy, lazy або dp - спосіб розбору LZ77, за замовчуванням lazy
    //   опція --split dp - ДП-розбиття на блоки
    //   опція --predict - прогноз наступного байта
    //   опція --model файл - класифікатор типу файла
    // unpack - розпакувати: папка, потім томи
    // measure - таблиці для звіту по файлах
    // train - навчити класифікатор: папка train, папка valid, файл моделі
    // classify - визначити тип файлів: файл моделі, потім файли
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
                "опції pack: --parser greedy, --parser lazy, --parser dp, --split dp, --predict, --model файл"
            );
            Console.WriteLine("unpack папка томи");
            Console.WriteLine("measure файли");
            Console.WriteLine("train папка_train папка_valid файл_моделі");
            Console.WriteLine("classify файл_моделі файли");
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
            int index = 1;

            // Опції на початку: --parser, --split, --predict, --model
            while (index < args.Length && args[index].StartsWith("--"))
            {
                string option = args[index];

                if (option == "--predict")
                {
                    settings.UsePrediction = true;
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
                else
                    throw new ArgumentException($"невідома опція {option} {value}");
            }

            if (args.Length - index < 2)
                throw new ArgumentException("потрібні ім'я архіву і хоча б один файл");

            string archivePath = args[index];
            var files = new List<ArchiveFile>();
            for (int i = index + 1; i < args.Length; i++)
                files.Add(new ArchiveFile(Path.GetFileName(args[i]), File.ReadAllBytes(args[i])));

            List<byte[]> volumes = ArchiveWriter.CreateArchive(files, GetPassword(), settings);

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

        // Розпаковує томи в папку
        static void Unpack(string[] args)
        {
            var volumes = new List<byte[]>();
            for (int i = 2; i < args.Length; i++)
                volumes.Add(File.ReadAllBytes(args[i]));

            List<ArchiveFile> files = ArchiveReader.ReadArchive(volumes, GetPassword());
            Extractor.SaveFiles(files, args[1]);
            Console.WriteLine($"Розпаковано файлів: {files.Count}");
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

        // Показує тип, який перцептрон визначив для кожного файла
        static void Classify(string[] args)
        {
            FileClassifier classifier = FileClassifier.Load(args[1]);

            for (int i = 2; i < args.Length; i++)
            {
                int type = classifier.Classify(File.ReadAllBytes(args[i]));
                Console.WriteLine(
                    $"{Path.GetFileName(args[i])}: тип {type}, {FileClassifier.classNames[type]}"
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
