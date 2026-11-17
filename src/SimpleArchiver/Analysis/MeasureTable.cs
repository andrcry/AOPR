using System.Diagnostics;

namespace SimpleArchiver
{
    // Команда measure: таблиці для звіту.
    // Перша таблиця - жадібний, лінивий і ДП-розбір LZ77 для кожного файла.
    // Друга - фіксоване і ДП-розбиття на блоки, з прогнозом байта і без.
    // Третя - розбір LZ77 за групами корпусу. Група файла береться з labels.csv у його папці
    public static class MeasureTable
    {
        // Три способи розбору, які порівнюємо
        static ParserKind[] parsers = { ParserKind.Greedy, ParserKind.Lazy, ParserKind.Dp };
        static string[] parserNames = { "жадібний", "лінивий", "ДП" };

        // Друкує таблиці на екран
        public static void Print(string[] args, int start)
        {
            foreach (string line in Build(args, start))
                Console.WriteLine(line);
        }

        // Складає рядки таблиць. Аргументи: файли або папки
        public static List<string> Build(string[] args, int start)
        {
            List<string> paths = InputFiles.Collect(args, start);
            if (paths.Count == 0)
                throw new ArgumentException("потрібен хоча б один файл");

            var settings = new Settings();
            var lines = new List<string>();

            // Розбір LZ77 кожного файла трьома способами. Результати запам'ятовуємо для таблиці груп
            var sizes = new long[paths.Count, parsers.Length];
            var times = new long[paths.Count, parsers.Length];
            var fileSizes = new long[paths.Count];

            lines.Add($"Розбір LZ77, блоки по {settings.BlockSize} байтів");
            lines.Add(
                "файл\tрозмір\tрозбір\tрезультат\tчас_мс\tлітерали\tпосилання\tвартість_бітів"
            );

            for (int f = 0; f < paths.Count; f++)
            {
                byte[] data = File.ReadAllBytes(paths[f]);
                fileSizes[f] = data.Length;

                for (int p = 0; p < parsers.Length; p++)
                {
                    ParseResult result = MeasureParser(data, parsers[p], settings);
                    sizes[f, p] = result.Size;
                    times[f, p] = result.Milliseconds;

                    lines.Add(
                        $"{Path.GetFileName(paths[f])}\t{data.Length}\t{parserNames[p]}\t{result.Size}\t{result.Milliseconds}\t"
                            + $"{result.Literals}\t{result.Matches}\t{result.FixedCost}"
                    );
                }
            }

            // Розмір блоків файла при фіксованому і ДП-розбитті, з прогнозом байта і без
            lines.Add("");
            lines.Add(
                "Розбиття на блоки і прогноз, розмір блоків разом із записами таблиці блоків"
            );
            lines.Add("файл\tфіксоване\tДП-розбиття\tфіксоване+прогноз\tДП-розбиття+прогноз");

            foreach (string path in paths)
            {
                byte[] data = File.ReadAllBytes(path);
                lines.Add(
                    $"{Path.GetFileName(path)}\t{MeasureSplit(data, false, false)}\t{MeasureSplit(data, true, false)}\t"
                        + $"{MeasureSplit(data, false, true)}\t{MeasureSplit(data, true, true)}"
                );
            }

            // Ті самі розміри і час розборів, але додані разом по кожній групі корпусу
            lines.AddRange(GroupTable(paths, fileSizes, sizes, times));
            return lines;
        }

        // Таблиця за групами: для кожного типу 0-6 сума розмірів і часу. Файли без мітки в labels.csv пропускаються
        static List<string> GroupTable(
            List<string> paths,
            long[] fileSizes,
            long[,] sizes,
            long[,] times
        )
        {
            var lines = new List<string>();
            var groupFiles = new int[FileClassifier.ClassCount];
            var groupBytes = new long[FileClassifier.ClassCount];
            var groupSizes = new long[FileClassifier.ClassCount, parsers.Length];
            var groupTimes = new long[FileClassifier.ClassCount, parsers.Length];
            int labeled = 0;

            for (int f = 0; f < paths.Count; f++)
            {
                int type = Dataset.TypeOf(paths[f]);
                if (type < 0)
                    continue;

                labeled++;
                groupFiles[type]++;
                groupBytes[type] += fileSizes[f];
                for (int p = 0; p < parsers.Length; p++)
                {
                    groupSizes[type, p] += sizes[f, p];
                    groupTimes[type, p] += times[f, p];
                }
            }

            if (labeled == 0)
                return lines; // немає labels.csv - групи невідомі

            lines.Add("");
            lines.Add("Розбір LZ77 за групами корпусу (групи з labels.csv)");
            lines.Add(
                "група\tфайлів\tрозмір\tжадібний\tжадібний_мс\tлінивий\tлінивий_мс\tДП\tДП_мс"
            );

            for (int type = 0; type < FileClassifier.ClassCount; type++)
            {
                if (groupFiles[type] == 0)
                    continue;

                string row =
                    $"{FileClassifier.classNames[type]}\t{groupFiles[type]}\t{groupBytes[type]}";
                for (int p = 0; p < parsers.Length; p++)
                    row += $"\t{groupSizes[type, p]}\t{groupTimes[type, p]}";
                lines.Add(row);
            }

            return lines;
        }

        // Розбирає файл блоками по BlockSize і рахує розмір, час, кількість літералів і посилань, вартість у бітах
        static ParseResult MeasureParser(byte[] data, ParserKind kind, Settings settings)
        {
            var result = new ParseResult();
            Stopwatch timer = Stopwatch.StartNew();

            for (int offset = 0; offset < data.Length; offset += settings.BlockSize)
            {
                int length = Math.Min(settings.BlockSize, data.Length - offset);
                var chunk = new byte[length];
                Array.Copy(data, offset, chunk, 0, length);

                List<Token> tokens = Lz77.Parse(chunk, kind, settings.MaxChain);
                result.FixedCost += Lz77.FixedCost(tokens);

                foreach (Token token in tokens)
                {
                    if (token.IsMatch)
                        result.Matches++;
                    else
                        result.Literals++;
                }

                // Якщо стиснення не вигідне, блок займає raw_len (NO_COMPRESSION)
                result.Size += Math.Min(BlockCoder.Encode(tokens).Length, chunk.Length);
            }

            result.Milliseconds = timer.ElapsedMilliseconds;
            return result;
        }

        // Розмір блоків файла при фіксованому або ДП-розбитті, з прогнозом або без
        static string MeasureSplit(byte[] data, bool dpSplit, bool prediction)
        {
            var settings = new Settings();
            settings.UsePrediction = prediction;

            List<PreparedBlock> blocks = dpSplit
                ? BlockSplitter.SplitDp(data, settings)
                : BlockSplitter.SplitFixed(data, settings, true);

            return BlockSplitter.TotalSize(blocks).ToString();
        }

        // Результат розбору одного файла
        class ParseResult
        {
            public long Size { get; set; } // розмір після стиснення
            public long Milliseconds { get; set; } // час розбору і кодування
            public long Literals { get; set; } // скільки літералів
            public long Matches { get; set; } // скільки посилань
            public long FixedCost { get; set; } // вартість розбору за фіксованими вартостями, біт
        }
    }
}
