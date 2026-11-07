namespace SimpleArchiver
{
    // Команди програми:
    // pack - створити архів: ім'я архіву, потім файли
    // unpack - розпакувати: папка, потім томи
    // Пароль береться зі змінної SARC_PASSWORD або запитується
    static class Program
    {
        // Читає команду і запускає потрібну дію
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (args.Length < 3)
            {
                PrintHelp();
                return 1;
            }

            try
            {
                if (args[0] == "pack")
                    Pack(args);
                else if (args[0] == "unpack")
                    Unpack(args);
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
            Console.WriteLine("pack архів.arc файли");
            Console.WriteLine("unpack папка томи");
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

        // Пакує файли в один том
        static void Pack(string[] args)
        {
            var files = new List<ArchiveFile>();
            for (int i = 2; i < args.Length; i++)
                files.Add(new ArchiveFile(Path.GetFileName(args[i]), File.ReadAllBytes(args[i])));

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                files,
                GetPassword(),
                new Settings()
            );

            File.WriteAllBytes(args[1], volumes[0]);
            Console.WriteLine($"Готово: {args[1]}, {volumes[0].Length} байтів");
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
    }
}
