namespace SimpleArchiver
{
    // Модуль атаки (п. 3.7 методички). Три частини:
    // 1. Базова атака: злам шифру Віженера / багатобайтового XOR з коротким ключем генетичним алгоритмом.
    //    Пристосованість - наскільки частоти байтів і біграм розшифрованого тексту близькі до природного тексту/коду.
    // 2. Просунута атака на метадані: без пароля читаємо лише відкриті заголовки томів (CiphertextLength)
    //    і за відбитком розмірів томів вгадуємо файл і його тип. Вирівнювання VolumePadding цей витік зменшує.
    // 3. Перевірка цілісності: змінюємо один байт архіву і переконуємося, що ArchiveReader відмовляє з E_HMAC
    public enum WeakCipherKind
    {
        Xor, // c = p XOR k
        Vigenere, // c = (p + k) mod 256 - байтовий шифр Віженера
    }

    // Модель «природного тексту»: частоти байтів і біграм з еталонних зразків (логарифми ймовірностей)
    public class ByteLanguageModel
    {
        readonly double[] logUni = new double[256]; // log2 P(байт)
        readonly double[] logBi = new double[256 * 256]; // log2 P(байт1, байт2)

        public int[] TopBytes { get; private set; } = Array.Empty<int>(); // найчастіші байти еталону

        // Навчає модель на зразках. Згладжування Лапласа, щоб не було log(0)
        public static ByteLanguageModel FromSamples(IEnumerable<byte[]> samples)
        {
            var uni = new double[256];
            var bi = new double[256 * 256];
            double uniTotal = 0;
            double biTotal = 0;

            foreach (byte[] sample in samples)
            {
                for (int i = 0; i < sample.Length; i++)
                {
                    uni[sample[i]]++;
                    uniTotal++;
                    if (i + 1 < sample.Length)
                    {
                        bi[sample[i] * 256 + sample[i + 1]]++;
                        biTotal++;
                    }
                }
            }

            if (uniTotal == 0)
                throw new ArgumentException("еталонні зразки для моделі тексту порожні");

            var model = new ByteLanguageModel();
            for (int b = 0; b < 256; b++)
                model.logUni[b] = Math.Log2((uni[b] + 0.5) / (uniTotal + 128.0));
            for (int p = 0; p < bi.Length; p++)
                model.logBi[p] = Math.Log2((bi[p] + 0.01) / (biTotal + 655.36));

            model.TopBytes = Enumerable
                .Range(0, 256)
                .OrderByDescending(b => uni[b])
                .Take(8)
                .ToArray();
            return model;
        }

        // Пристосованість: мінус перехресна ентропія (біт на байт) для байтів і для біграм.
        // Чим ближчі частоти розшифрованого тексту до еталону, тим більше значення (максимум близько -10)
        public double Score(byte[] text)
        {
            if (text.Length < 2)
                return double.NegativeInfinity;

            double uniSum = 0;
            double biSum = 0;
            for (int i = 0; i < text.Length; i++)
            {
                uniSum += logUni[text[i]];
                if (i + 1 < text.Length)
                    biSum += logBi[text[i] * 256 + text[i + 1]];
            }

            return uniSum / text.Length + biSum / (text.Length - 1);
        }
    }

    // Налаштування ГА для зламу ключа
    public class CipherGaOptions
    {
        public int PopulationSize { get; set; } = 60;
        public int Generations { get; set; } = 150;
        public int TournamentSize { get; set; } = 3;
        public double CrossoverRate { get; set; } = 0.9;
        public int ElitismCount { get; set; } = 2;
        public int MaxKeyLength { get; set; } = 16;
        public int SampleBytes { get; set; } = 4096; // скільки байтів шифротексту бере пристосованість
        public int StallGenerations { get; set; } = 40; // зупинка, якщо найкраща не змінюється стільки поколінь
        public int Seed { get; set; } = 2001;
        public int? KnownKeyLength { get; set; } // якщо null - довжина ключа оцінюється індексом збігу
    }

    // Результат зламу шифру
    public class CipherAttackResult
    {
        public WeakCipherKind Kind { get; set; }
        public int EstimatedKeyLength { get; set; }
        public byte[] RecoveredKey { get; set; } = Array.Empty<byte>();
        public byte[] Decrypted { get; set; } = Array.Empty<byte>();
        public double BestFitness { get; set; }
        public int Generations { get; set; }
        public int Evaluations { get; set; }
        public List<double> BestByGeneration { get; } = new List<double>();
        public double[] IndexOfCoincidence { get; set; } = Array.Empty<double>(); // для довжин 1..MaxKeyLength
    }

    // Результат атаки на метадані для одного способу вирівнювання
    public class MetadataLeakResult
    {
        public string Mode { get; set; } = "";
        public int Files { get; set; }
        public int DistinctFingerprints { get; set; } // різних відбитків розмірів томів
        public int UniquelyIdentified { get; set; } // файлів, які впізнано однозначно
        public double TypeGuessAccuracy { get; set; } // ймовірність вгадати тип за відбитком
        public double LeakBits { get; set; } // ентропія відбитка, біт
        public long VisibleBytes { get; set; } // сума видимих розмірів томів
        public List<string> Fingerprints { get; } = new List<string>(); // відбиток кожного файла
        public List<int> GuessedTypes { get; } = new List<int>(); // тип, який назве атакувальник
    }

    // Результат однієї спроби змінити байт архіву
    public class TamperResult
    {
        public int Volume { get; set; }
        public int Offset { get; set; }
        public string Region { get; set; } = "";
        public string Code { get; set; } = "";
        public string Expected { get; set; } = ErrorCodes.Hmac; // очікуваний код відмови
        public bool RejectedWithHmac => Code == ErrorCodes.Hmac;
        public bool Rejected => Code != "OK"; // архів не прийнято
        public bool AsExpected => Code == Expected;
    }

    public static class WeakCipherAttack
    {
        // ---------- 1. Слабкий шифр і його злам генетичним алгоритмом ----------

        // Шифрує байти коротким ключем, що повторюється
        public static byte[] Encrypt(byte[] plain, byte[] key, WeakCipherKind kind)
        {
            if (key.Length == 0)
                throw new ArgumentException("ключ порожній");

            var result = new byte[plain.Length];
            for (int i = 0; i < plain.Length; i++)
            {
                byte k = key[i % key.Length];
                result[i] = kind == WeakCipherKind.Xor ? (byte)(plain[i] ^ k) : (byte)(plain[i] + k);
            }
            return result;
        }

        // Розшифровує байти ключем
        public static byte[] Decrypt(byte[] cipher, byte[] key, WeakCipherKind kind)
        {
            if (key.Length == 0)
                throw new ArgumentException("ключ порожній");

            var result = new byte[cipher.Length];
            for (int i = 0; i < cipher.Length; i++)
            {
                byte k = key[i % key.Length];
                result[i] = kind == WeakCipherKind.Xor ? (byte)(cipher[i] ^ k) : (byte)(cipher[i] - k);
            }
            return result;
        }

        // Ключовий байт, при якому байт шифротексту c перетворюється на байт відкритого тексту p
        static byte KeyFor(byte c, byte p, WeakCipherKind kind) =>
            kind == WeakCipherKind.Xor ? (byte)(c ^ p) : (byte)(c - p);

        // Середній індекс збігу стовпців для кожної довжини ключа 1..maxLength.
        // Для правильної довжини кожен стовпець зашифровано одним байтом, і частоти зберігають «нерівність» тексту
        public static double[] IndexOfCoincidence(byte[] cipher, int maxLength)
        {
            var result = new double[maxLength];
            for (int length = 1; length <= maxLength; length++)
            {
                double sum = 0;
                for (int column = 0; column < length; column++)
                {
                    var counts = new int[256];
                    int n = 0;
                    for (int i = column; i < cipher.Length; i += length)
                    {
                        counts[cipher[i]]++;
                        n++;
                    }

                    if (n < 2)
                        continue;

                    double pairs = 0;
                    foreach (int c in counts)
                        pairs += (double)c * (c - 1);
                    sum += pairs / ((double)n * (n - 1));
                }
                result[length - 1] = sum / length;
            }
            return result;
        }

        // Оцінка довжини ключа: найменша довжина, чий індекс збігу не менший за 90% найбільшого.
        // Кратні справжньої довжини теж мають високий індекс, тому беремо найменшу
        public static int EstimateKeyLength(byte[] cipher, int maxLength)
        {
            maxLength = Math.Max(1, Math.Min(maxLength, cipher.Length / 4));
            double[] ic = IndexOfCoincidence(cipher, maxLength);
            double best = ic.Max();
            for (int length = 1; length <= maxLength; length++)
            {
                if (ic[length - 1] >= 0.9 * best)
                    return length;
            }
            return 1;
        }

        // Злам ключа генетичним алгоритмом.
        // Хромосома - байти ключа. Турнірна селекція, рівномірний кросовер, мутація
        // (випадковий байт або «частотна» підказка: найчастіший байт стовпця -> найчастіший байт еталону), елітизм
        public static CipherAttackResult BreakWithGa(
            byte[] cipher,
            ByteLanguageModel model,
            WeakCipherKind kind,
            CipherGaOptions options
        )
        {
            if (cipher.Length < 16)
                throw new ArgumentException("шифротекст закороткий для атаки");

            var random = new Random(options.Seed);
            byte[] sample =
                cipher.Length > options.SampleBytes ? cipher[..options.SampleBytes] : cipher;

            var result = new CipherAttackResult { Kind = kind };
            int maxLength = Math.Max(1, Math.Min(options.MaxKeyLength, sample.Length / 4));
            result.IndexOfCoincidence = IndexOfCoincidence(sample, maxLength);
            int keyLength = options.KnownKeyLength ?? EstimateKeyLength(sample, maxLength);
            result.EstimatedKeyLength = keyLength;

            // Найчастіші байти кожного стовпця шифротексту - для частотної мутації
            var columnTop = new int[keyLength][];
            for (int column = 0; column < keyLength; column++)
            {
                var counts = new int[256];
                for (int i = column; i < sample.Length; i += keyLength)
                    counts[sample[i]]++;
                columnTop[column] = Enumerable
                    .Range(0, 256)
                    .OrderByDescending(b => counts[b])
                    .Take(4)
                    .ToArray();
            }

            int evaluations = 0;
            double Fitness(byte[] key)
            {
                evaluations++;
                return model.Score(Decrypt(sample, key, kind));
            }

            void Mutate(byte[] key, double rate)
            {
                for (int g = 0; g < key.Length; g++)
                {
                    if (random.NextDouble() >= rate)
                        continue;

                    if (random.NextDouble() < 0.5)
                    {
                        key[g] = (byte)random.Next(256);
                    }
                    else
                    {
                        byte c = (byte)columnTop[g][random.Next(columnTop[g].Length)];
                        byte p = (byte)model.TopBytes[random.Next(model.TopBytes.Length)];
                        key[g] = KeyFor(c, p, kind);
                    }
                }
            }

            // Покоління 0: випадкові ключі
            var population = new List<(byte[] Key, double Fit)>();
            for (int i = 0; i < options.PopulationSize; i++)
            {
                var key = new byte[keyLength];
                random.NextBytes(key);
                population.Add((key, Fitness(key)));
            }

            double mutationRate = Math.Max(1.0 / keyLength, 0.15);
            double bestSoFar = double.NegativeInfinity;
            int stall = 0;
            int generation = 0;

            for (; generation <= options.Generations; generation++)
            {
                population.Sort((a, b) => b.Fit.CompareTo(a.Fit));
                result.BestByGeneration.Add(population[0].Fit);

                if (population[0].Fit > bestSoFar + 1e-12)
                {
                    bestSoFar = population[0].Fit;
                    stall = 0;
                }
                else if (++stall >= options.StallGenerations)
                {
                    break;
                }

                if (generation == options.Generations)
                    break;

                // Строгий елітизм: найкращі переходять без змін
                var next = new List<(byte[] Key, double Fit)>();
                for (int e = 0; e < Math.Min(options.ElitismCount, population.Count); e++)
                    next.Add(((byte[])population[e].Key.Clone(), population[e].Fit));

                while (next.Count < options.PopulationSize)
                {
                    byte[] a = Tournament(population, options.TournamentSize, random);
                    byte[] b = Tournament(population, options.TournamentSize, random);
                    var child = (byte[])a.Clone();

                    if (random.NextDouble() < options.CrossoverRate)
                    {
                        for (int g = 0; g < keyLength; g++)
                            child[g] = random.NextDouble() < 0.5 ? a[g] : b[g];
                    }

                    Mutate(child, mutationRate);
                    next.Add((child, Fitness(child)));
                }

                population = next;
            }

            population.Sort((a, b) => b.Fit.CompareTo(a.Fit));
            result.RecoveredKey = population[0].Key;
            result.BestFitness = population[0].Fit;
            result.Decrypted = Decrypt(cipher, population[0].Key, kind);
            result.Generations = Math.Min(generation, options.Generations);
            result.Evaluations = evaluations;
            return result;
        }

        // Турнірна селекція
        static byte[] Tournament(List<(byte[] Key, double Fit)> population, int size, Random random)
        {
            var best = population[random.Next(population.Count)];
            for (int i = 1; i < size; i++)
            {
                var other = population[random.Next(population.Count)];
                if (other.Fit > best.Fit)
                    best = other;
            }
            return best.Key;
        }

        // Частка байтів, які збіглися з відкритим текстом
        public static double Accuracy(byte[] expected, byte[] actual)
        {
            if (expected.Length == 0 || expected.Length != actual.Length)
                return 0;

            int same = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] == actual[i])
                    same++;
            }
            return (double)same / expected.Length;
        }

        // ---------- 2. Атака на метадані (без розшифрування) ----------

        // Відбиток архіву, видимий без пароля: CiphertextLength кожного тому з відкритого заголовка
        public static string Fingerprint(List<byte[]> volumes)
        {
            var lengths = new List<string>();
            foreach (byte[] volume in volumes)
                lengths.Add(VolumeHeader.Parse(volume).CiphertextLength.ToString());
            return string.Join("+", lengths);
        }

        // Атакувальник знає набір файлів-кандидатів і сам пакує їх тією ж програмою
        // (довжини не залежать від пароля, солі і nonce). Потім за відбитком перехопленого архіву
        // шукає кандидатів з тим самим відбитком і називає найчастіший серед них тип.
        // types[i] - справжній тип файла i (0-6)
        public static MetadataLeakResult AttackMetadata(
            List<ArchiveFile> files,
            int[] types,
            Settings settings,
            string mode,
            string password
        )
        {
            if (files.Count == 0 || files.Count != types.Length)
                throw new ArgumentException("потрібні файли і їхні типи однакової кількості");

            var result = new MetadataLeakResult { Mode = mode, Files = files.Count };
            foreach (ArchiveFile file in files)
            {
                var single = new List<ArchiveFile> { file };
                List<byte[]> volumes = ArchiveWriter.CreateArchive(single, password, settings);
                result.Fingerprints.Add(Fingerprint(volumes));
                foreach (byte[] volume in volumes)
                    result.VisibleBytes += volume.Length;
            }

            var groups = new Dictionary<string, List<int>>();
            for (int i = 0; i < files.Count; i++)
            {
                if (!groups.TryGetValue(result.Fingerprints[i], out var list))
                    groups[result.Fingerprints[i]] = list = new List<int>();
                list.Add(i);
            }

            double accuracy = 0;
            double leak = 0;
            for (int i = 0; i < files.Count; i++)
            {
                List<int> group = groups[result.Fingerprints[i]];
                if (group.Count == 1)
                    result.UniquelyIdentified++;

                // Найчастіший тип серед кандидатів з тим самим відбитком (нічия - менший номер типу)
                int guess = group
                    .GroupBy(j => types[j])
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key)
                    .First()
                    .Key;
                result.GuessedTypes.Add(guess);

                // Ймовірність вгадати тип: частка кандидатів групи з правильним типом
                accuracy += (double)group.Count(j => types[j] == types[i]) / group.Count;
                leak += Math.Log2((double)files.Count / group.Count);
            }

            result.DistinctFingerprints = groups.Count;
            result.TypeGuessAccuracy = accuracy / files.Count;
            result.LeakBits = leak / files.Count;
            return result;
        }

        // ---------- 3. Перевірка цілісності ----------

        // Змінює по одному байту в різних частинах томів і пробує прочитати архів.
        // Очікується відмова E_HMAC: пломба покриває заголовок і шифротекст (Encrypt-then-MAC),
        // а зміна солі дає інші ключі
        public static List<TamperResult> TamperAttack(
            List<byte[]> volumes,
            string password,
            int randomPositions,
            int seed
        )
        {
            var random = new Random(seed);
            var results = new List<TamperResult>();

            for (int v = 0; v < volumes.Count; v++)
            {
                int length = volumes[v].Length;
                int cipherEnd = length - VolumeHeader.MacSize;
                var targets = new List<(int Offset, string Region)>
                {
                    (9, "сіль"),
                    (25, "nonce"),
                    (VolumeHeader.Size, "шифротекст: перший байт"),
                    (cipherEnd - 1, "шифротекст: останній байт"),
                    (length - 1, "пломба HMAC"),
                };
                for (int r = 0; r < randomPositions && cipherEnd > VolumeHeader.Size; r++)
                    targets.Add((random.Next(VolumeHeader.Size, cipherEnd), "шифротекст: випадковий байт"));

                foreach (var (offset, region) in targets)
                {
                    var copy = new List<byte[]>();
                    foreach (byte[] volume in volumes)
                        copy.Add((byte[])volume.Clone());
                    copy[v][offset] ^= (byte)(1 + random.Next(255));

                    // Сіль однакова в усіх томах архіву. У багатотомному архіві зміну солі
                    // ще до HMAC ловить перевірка набору томів (томи «з різних архівів») - E_VOLUMES
                    string expected =
                        region == "сіль" && volumes.Count > 1 ? ErrorCodes.Volumes : ErrorCodes.Hmac;

                    results.Add(
                        new TamperResult
                        {
                            Volume = v,
                            Offset = offset,
                            Region = region,
                            Code = ReadCode(copy, password),
                            Expected = expected,
                        }
                    );
                }
            }

            return results;
        }

        // Пробує прочитати архів і повертає код помилки або "OK"
        public static string ReadCode(List<byte[]> volumes, string password)
        {
            try
            {
                ArchiveReader.ReadArchive(volumes, password);
                return "OK";
            }
            catch (ArchiveException e)
            {
                return e.Code;
            }
        }

        // Ключ як рядок шістнадцяткових цифр
        public static string Hex(byte[] key) => Convert.ToHexString(key);
    }
}
