namespace SimpleArchiver
{
    // Таблиця для звіту: аналіз витоку метаданих і кількісне порівняння захисту.
    // Без пароля сторонній бачить лише відкритий заголовок тому: кількість томів і їхні розміри.
    // Таблиця файлів і розміри блоків зашифровані. Тому витікає лише розмір архіву.
    // Таблиця показує, скільки інформації дає цей розмір без вирівнювання і з вирівнюванням,
    // і скільки байтів коштує вирівнювання
    public static class LeakTable
    {
        const int defaultPadUnit = 4096; // крок вирівнювання, якщо не задано опцією --pad

        // Друкує таблицю на екран
        public static void Print(string[] args, int start)
        {
            foreach (string line in Build(args, start))
                Console.WriteLine(line);
        }

        // Складає рядки таблиці. Аргументи: опція --pad байти, потім файли або папки.
        // Кожен файл пакується в окремий архів, ніби його передають окремо
        public static List<string> Build(string[] args, int start)
        {
            int padUnit = defaultPadUnit;
            int index = start;

            if (index + 1 < args.Length && args[index] == "--pad")
            {
                padUnit = int.Parse(args[index + 1]);
                index += 2;
            }

            List<string> paths = InputFiles.Collect(args, index);
            if (paths.Count == 0)
                throw new ArgumentException("потрібен хоча б один файл");

            // Крок 1. Три способи захисту, які порівнюємо
            var plain = new Settings();
            var byUnit = new Settings();
            byUnit.PadUnit = padUnit;
            var byPower = new Settings();
            byPower.PadToPowerOfTwo = true;

            Settings[] modes = { plain, byUnit, byPower };
            string[] names =
            {
                "без вирівнювання",
                $"кратно {padUnit} байтам",
                "до степеня двійки",
            };

            // Крок 2. Видимий розмір архіву кожного файла в кожному режимі
            var sizes = new long[modes.Length, paths.Count];
            var lines = new List<string>();
            lines.Add("Видимі розміри архівів, байтів");
            lines.Add($"файл\tрозмір_файла\t{names[0]}\t{names[1]}\t{names[2]}");

            for (int f = 0; f < paths.Count; f++)
            {
                byte[] data = File.ReadAllBytes(paths[f]);
                var files = new List<ArchiveFile>();
                files.Add(new ArchiveFile(Path.GetFileName(paths[f]), data));

                string row = $"{Path.GetFileName(paths[f])}\t{data.Length}";
                for (int m = 0; m < modes.Length; m++)
                {
                    sizes[m, f] = VisibleSize(ArchiveWriter.BuildPayloads(files, modes[m]));
                    row += $"\t{sizes[m, f]}";
                }
                lines.Add(row);
            }

            // Крок 3. Кількісне порівняння способів захисту
            long plainTotal = Total(sizes, 0, paths.Count);

            lines.Add("");
            lines.Add("Порівняння захисту");
            lines.Add(
                "спосіб\tрізних_розмірів\tвпізнаних_файлів\tвиток_бітів\tдодано_байтів\tдодано_відсотків"
            );

            for (int m = 0; m < modes.Length; m++)
            {
                var counts = CountSizes(sizes, m, paths.Count);
                long added = Total(sizes, m, paths.Count) - plainTotal;
                double percent = plainTotal == 0 ? 0 : 100.0 * added / plainTotal;

                lines.Add(
                    $"{names[m]}\t{counts.Count}\t{UniqueFiles(counts)}\t{LeakBits(counts, paths.Count):F3}\t{added}\t{percent:F2}"
                );
            }

            return lines;
        }

        // Видимий розмір архіву: для кожного тому заголовок 41 байт, навантаження і пломба HMAC 32 байти
        static long VisibleSize(List<Payload> payloads)
        {
            long size = 0;
            foreach (Payload payload in payloads)
                size += VolumeHeader.Size + payload.ToBytes().Length + VolumeHeader.MacSize;
            return size;
        }

        // Сумарний видимий розмір усіх архівів у режимі m
        static long Total(long[,] sizes, int m, int count)
        {
            long sum = 0;
            for (int f = 0; f < count; f++)
                sum += sizes[m, f];
            return sum;
        }

        // Скільки файлів має кожен видимий розмір у режимі m
        static Dictionary<long, int> CountSizes(long[,] sizes, int m, int count)
        {
            var counts = new Dictionary<long, int>();
            for (int f = 0; f < count; f++)
            {
                if (counts.ContainsKey(sizes[m, f]))
                    counts[sizes[m, f]]++;
                else
                    counts[sizes[m, f]] = 1;
            }
            return counts;
        }

        // Скільки файлів мають розмір, якого немає в жодного іншого файла: такий файл можна впізнати за розміром
        static int UniqueFiles(Dictionary<long, int> counts)
        {
            int unique = 0;
            foreach (int count in counts.Values)
            {
                if (count == 1)
                    unique++;
            }
            return unique;
        }

        // Витік у бітах: ентропія Шеннона видимих розмірів, H = -sum p * log2(p).
        // Якщо всі розміри однакові - 0 бітів, розмір нічого не каже.
        // Якщо всі різні - log2(кількість файлів) бітів, за розміром можна впізнати кожен файл
        static double LeakBits(Dictionary<long, int> counts, int total)
        {
            double h = 0;
            foreach (int count in counts.Values)
            {
                double p = (double)count / total;
                h -= p * Math.Log2(p);
            }
            return h;
        }
    }
}
