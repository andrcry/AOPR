namespace SimpleArchiver
{
    // Збирає список файлів з аргументів команди. Папку розкриває в усі файли всередині неї
    public static class InputFiles
    {
        // Повертає файли з аргументів, починаючи з номера start.
        // Файли з папок беруться за абеткою. Приховані файли (.DS_Store, .gitkeep) пропускаються
        public static List<string> Collect(string[] args, int start)
        {
            var files = new List<string>();

            for (int i = start; i < args.Length; i++)
            {
                if (Directory.Exists(args[i]))
                {
                    string[] inside = Directory.GetFiles(args[i], "*", SearchOption.AllDirectories);
                    Array.Sort(inside, StringComparer.Ordinal);

                    foreach (string file in inside)
                    {
                        if (!Path.GetFileName(file).StartsWith("."))
                            files.Add(file);
                    }
                }
                else if (File.Exists(args[i]))
                {
                    files.Add(args[i]);
                }
                else
                {
                    throw new FileNotFoundException($"файл або папку не знайдено: {args[i]}");
                }
            }

            return files;
        }
    }
}
