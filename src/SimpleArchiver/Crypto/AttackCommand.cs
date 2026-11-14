namespace SimpleArchiver
{
    // Команда attack: демонстрація модуля атаки на корпусі.
    // attack [--cipher xor|vigenere] [--key текст] [--seed N] [--pad байти] [файли_або_папки]
    // Без файлів береться data/corpus. Типи файлів - з labels.csv у папці файлів
    public static class AttackCommand
    {
        const string demoPassword = "attack-demo-2026"; // пароль архівів для частин 2 і 3

        public static void Print(string[] args, int start)
        {
            WeakCipherKind[] kinds = { WeakCipherKind.Xor, WeakCipherKind.Vigenere };
            string? keyText = null;
            int seed = 2001;
            int padUnit = 4096;
            int index = start;

            while (index < args.Length && args[index].StartsWith("--"))
            {
                if (index + 1 >= args.Length)
                    throw new ArgumentException($"після {args[index]} потрібне значення");

                string value = args[index + 1];
                switch (args[index])
                {
                    case "--cipher":
                        kinds = value switch
                        {
                            "xor" => new[] { WeakCipherKind.Xor },
                            "vigenere" => new[] { WeakCipherKind.Vigenere },
                            _ => throw new ArgumentException("--cipher: xor або vigenere"),
                        };
                        break;
                    case "--key":
                        keyText = value;
                        break;
                    case "--seed":
                        seed = int.Parse(value);
                        break;
                    case "--pad":
                        padUnit = int.Parse(value);
                        break;
                    default:
                        throw new ArgumentException($"невідома опція {args[index]}");
                }
                index += 2;
            }

            string[] pathArgs = index < args.Length ? args[index..] : new[] { "data/corpus" };
            List<string> paths = InputFiles.Collect(pathArgs, 0);
            if (paths.Count == 0)
                throw new ArgumentException("потрібен хоча б один файл");

            var types = new int[paths.Count];
            for (int i = 0; i < paths.Count; i++)
                types[i] = LabelOf(paths[i]);

            Console.WriteLine($"Файлів: {paths.Count} (labels.csv виключено)");
            Console.WriteLine();

            CipherPart(paths, types, kinds, keyText, seed);
            MetadataPart(paths, types, padUnit);
            IntegrityPart(paths, seed);
        }

        // Тип файла з labels.csv у його папці, -1 - невідомо
        static int LabelOf(string path)
        {
            string labels = Path.Combine(Path.GetDirectoryName(path) ?? ".", "labels.csv");
            if (!File.Exists(labels))
                return -1;

            return Dataset.ReadLabels(labels).TryGetValue(Path.GetFileName(path), out int type)
                ? type
                : -1;
        }

        // Частина 1. Злам XOR / Віженера генетичним алгоритмом
        static void CipherPart(
            List<string> paths,
            int[] types,
            WeakCipherKind[] kinds,
            string? keyText,
            int seed
        )
        {
            Console.WriteLine("1. Злам шифру з коротким ключем генетичним алгоритмом");

            // Ціль - перший природний текст; еталон частот - інші тексти і код (без цілі)
            var texts = new List<int>();
            for (int i = 0; i < paths.Count; i++)
            {
                bool isText = types[i] == 0 || (types[i] < 0 && paths[i].EndsWith(".txt"));
                if (isText)
                    texts.Add(i);
            }
            if (texts.Count == 0)
                texts.Add(0);

            int target = texts[0];
            byte[] plain = File.ReadAllBytes(paths[target]);
            var reference = new List<byte[]>();
            for (int i = 0; i < paths.Count; i++)
            {
                if (i != target && (types[i] == 0 || types[i] == 1))
                    reference.Add(File.ReadAllBytes(paths[i]));
            }
            if (reference.Count == 0)
            {
                // Еталону немає: модель - друга половина цілі, атакуємо першу
                reference.Add(plain[(plain.Length / 2)..]);
                plain = plain[..(plain.Length / 2)];
            }

            ByteLanguageModel model = ByteLanguageModel.FromSamples(reference);
            // Окремий генератор для ключа, щоб ключ не збігався з початковою популяцією ГА (той самий seed)
            var random = new Random(unchecked(seed * 7919 + 104729));
            byte[] key;
            if (keyText != null)
            {
                key = System.Text.Encoding.UTF8.GetBytes(keyText);
            }
            else
            {
                key = new byte[7];
                random.NextBytes(key);
            }

            Console.WriteLine(
                $"Ціль: {Path.GetFileName(paths[target])} ({plain.Length} байтів), еталон частот: {reference.Count} файлів"
            );
            Console.WriteLine(
                "шифр\tсправжній_ключ\tоцінена_довжина\tзнайдений_ключ\tпоколінь\tобчислень\tпристосованість\tвідновлено_байтів"
            );

            foreach (WeakCipherKind kind in kinds)
            {
                byte[] cipher = WeakCipherAttack.Encrypt(plain, key, kind);
                var options = new CipherGaOptions { Seed = seed };
                CipherAttackResult result = WeakCipherAttack.BreakWithGa(cipher, model, kind, options);
                double accuracy = WeakCipherAttack.Accuracy(plain, result.Decrypted);

                Console.WriteLine(
                    $"{kind}\t{WeakCipherAttack.Hex(key)}\t{result.EstimatedKeyLength}\t{WeakCipherAttack.Hex(result.RecoveredKey)}\t{result.Generations}\t{result.Evaluations}\t{result.BestFitness:F3}\t{accuracy * 100:F2}%"
                );
            }

            Console.WriteLine();
        }

        // Частина 2. Атака на метадані: відбиток CiphertextLength і захист вирівнюванням
        static void MetadataPart(List<string> paths, int[] types, int padUnit)
        {
            Console.WriteLine("2. Атака на метадані без розшифрування (CiphertextLength з VolumeHeader)");

            var files = new List<ArchiveFile>();
            var known = new List<int>();
            for (int i = 0; i < paths.Count; i++)
            {
                if (types[i] < 0)
                    continue;
                files.Add(new ArchiveFile(Path.GetFileName(paths[i]), File.ReadAllBytes(paths[i])));
                known.Add(types[i]);
            }

            if (files.Count == 0)
            {
                Console.WriteLine("Немає labels.csv - типи невідомі, частину пропущено.");
                Console.WriteLine();
                return;
            }

            var plain = new Settings();
            var byUnit = new Settings { PadUnit = padUnit };
            var byPower = new Settings { PadToPowerOfTwo = true };
            var modes = new (Settings Settings, string Name)[]
            {
                (plain, "без вирівнювання"),
                (byUnit, $"кратно {padUnit} байтам"),
                (byPower, "до степеня двійки"),
            };

            Console.WriteLine(
                "спосіб\tфайлів\tрізних_відбитків\tвпізнано_файлів\tймовірність_вгадати_тип\tвиток_бітів\tвидимих_байтів\tдодано_байтів"
            );
            long baseBytes = 0;
            foreach (var (settings, name) in modes)
            {
                MetadataLeakResult r = WeakCipherAttack.AttackMetadata(
                    files,
                    known.ToArray(),
                    settings,
                    name,
                    demoPassword
                );
                if (baseBytes == 0)
                    baseBytes = r.VisibleBytes;

                Console.WriteLine(
                    $"{name}\t{r.Files}\t{r.DistinctFingerprints}\t{r.UniquelyIdentified}\t{r.TypeGuessAccuracy:F3}\t{r.LeakBits:F3}\t{r.VisibleBytes}\t{r.VisibleBytes - baseBytes}"
                );
            }

            Console.WriteLine();
        }

        // Частина 3. Зміна одного байта архіву - очікується відмова E_HMAC
        static void IntegrityPart(List<string> paths, int seed)
        {
            Console.WriteLine("3. Перевірка цілісності: зміна одного байта архіву");

            var files = new List<ArchiveFile>();
            foreach (string path in paths)
                files.Add(new ArchiveFile(Path.GetFileName(path), File.ReadAllBytes(path)));

            List<byte[]> volumes = ArchiveWriter.CreateArchive(files, demoPassword, new Settings());
            Console.WriteLine($"Неушкоджений архів ({volumes.Count} томів): {WeakCipherAttack.ReadCode(volumes, demoPassword)}");

            List<TamperResult> results = WeakCipherAttack.TamperAttack(volumes, demoPassword, 3, seed);
            Console.WriteLine("область\tспроб\tочікуваний_код\tвідхилено\tз_очікуваним_кодом\tотримані_коди");
            foreach (var group in results.GroupBy(r => r.Region))
            {
                var codes = group.Select(r => r.Code).Distinct().ToList();
                var expected = group.Select(r => r.Expected).Distinct().ToList();
                Console.WriteLine(
                    $"{group.Key}\t{group.Count()}\t{string.Join(",", expected)}\t{group.Count(r => r.Rejected)}\t{group.Count(r => r.AsExpected)}\t{string.Join(",", codes)}"
                );
            }

            Console.WriteLine(
                $"Усього змінено архівів: {results.Count}; відхилено: {results.Count(r => r.Rejected)}; "
                    + $"з кодом E_HMAC: {results.Count(r => r.RejectedWithHmac)}; з очікуваним кодом: {results.Count(r => r.AsExpected)}"
            );
            Console.WriteLine();
        }
    }
}
