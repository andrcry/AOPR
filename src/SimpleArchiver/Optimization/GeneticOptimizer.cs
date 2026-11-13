using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SimpleArchiver
{
    // Хромосома для оптимізації параметрів архіватора (базовий рівень: 3 гени)
    public class GaChromosome
    {
        public int MaxChain { get; set; } = 64;
        public int BlockSize { get; set; } = Settings.teamBlockSize;
        public int SplitUnit { get; set; } = 2048;

        public GaChromosome Clone() =>
            new GaChromosome
            {
                MaxChain = MaxChain,
                BlockSize = BlockSize,
                SplitUnit = SplitUnit,
            };

        // Перетворює хромосому на налаштування архіватора з урахуванням team_config.json
        public Settings ToSettings(TeamConfig config, string? classifierPath = null)
        {
            return new Settings
            {
                MaxChain = MaxChain,
                BlockSize = BlockSize,
                SplitUnit = SplitUnit,
                MaxVolumeBytes = config.MaxVolumeBytes,
                TypePriorities = (int[])config.TypePriorities.Clone(),
                ClassifierPath = classifierPath,
            };
        }

        public string GetSignature() => $"{MaxChain}|{BlockSize}|{SplitUnit}";

        public string Describe() =>
            $"MaxChain={MaxChain}, BlockSize={BlockSize}, SplitUnit={SplitUnit}";
    }

    // Оцінена особина популяції
    public class GaIndividual
    {
        public GaChromosome Chromosome { get; }
        public QEvaluation Evaluation { get; }
        public double Fitness => Evaluation.Q;

        public GaIndividual(GaChromosome chromosome, QEvaluation evaluation)
        {
            Chromosome = chromosome;
            Evaluation = evaluation;
        }
    }

    // Запис одного покоління ГА для журналу experiments/ga_generations.tsv
    public class GaGenerationRecord
    {
        public int Generation { get; set; }
        public int Evaluations { get; set; }
        public double BestQ { get; set; }
        public double AverageQ { get; set; }
        public double BestS { get; set; }
        public double BestT { get; set; }
        public long BestArchiveBytes { get; set; }
        public double BestElapsedSeconds { get; set; }
        public string BestParams { get; set; } = "";
    }

    // Параметри запуску генетичного алгоритму
    public class GaRunOptions
    {
        public int PopulationSize { get; set; } = 8;
        public int Generations { get; set; } = 5;
        public int TournamentSize { get; set; } = 3;
        public double CrossoverRate { get; set; } = 0.85;
        public double MutationRate { get; set; } = 0.25;
        public int ElitismCount { get; set; } = 1;
        public int Seed { get; set; } = 2001;
        public string LogPath { get; set; } = "experiments/ga_generations.tsv";
        public string? ClassifierPath { get; set; }
    }

    // Результат одного методу оптимізації (ГА або випадкового пошуку)
    public class OptimizationResult
    {
        public string AlgorithmName { get; set; } = "";
        public GaIndividual Best { get; set; } = null!;
        public int Evaluations { get; set; }
        public double TotalElapsedSeconds { get; set; }
        public List<GaGenerationRecord> History { get; set; } = new();
        public List<GaIndividual> Evaluated { get; set; } = new();
    }

    // Звіт порівняння ГА та випадкового пошуку за однакового бюджету Q
    public class GaComparisonReport
    {
        public OptimizationResult Genetic { get; set; } = null!;
        public OptimizationResult RandomSearch { get; set; } = null!;
        public string TableText { get; set; } = "";
        public string GenerationsLogPath { get; set; } = "";
    }

    // Блок оптимізації параметрів архіватора (п. 3.6, п. 11.4, п. 12)
    public static class GeneticOptimizer
    {
        public static readonly int[] MaxChainCandidates = { 4, 8, 16, 32, 64, 128, 256 };
        public static readonly int[] BaseBlockSizeCandidates = { 2048, 4096, 8192, 16384, 32768, 65536 };
        public static readonly int[] SplitUnitCandidates = { 512, 1024, 2048, 4096 };

        // Завантажує корпус із файлів/папок, обов'язково виключаючи labels.csv
        public static List<ArchiveFile> LoadCorpus(string[] paths)
        {
            List<string> collected = InputFiles.Collect(paths, 0);
            var corpus = new List<ArchiveFile>();

            foreach (string filePath in collected)
            {
                string fileName = Path.GetFileName(filePath);
                if (string.Equals(fileName, "labels.csv", StringComparison.OrdinalIgnoreCase))
                    continue;

                corpus.Add(new ArchiveFile(fileName, File.ReadAllBytes(filePath)));
            }

            if (corpus.Count == 0)
                throw new ArgumentException("корпус порожній: не знайдено жодного файла (без урахування labels.csv)");

            return corpus;
        }

        // Обчислює допустимі розміри блока, що гарантовано вміщуються в том max_volume_bytes
        public static int[] ValidBlockSizes(TeamConfig config, List<ArchiveFile> corpus)
        {
            int tableSize = 0;
            foreach (ArchiveFile file in corpus)
                tableSize += new FileRecord { Name = file.Name }.SizeInTable();

            int maxSafeBlock =
                config.MaxVolumeBytes - VolumePacker.FixedOverhead - tableSize - BlockRecord.SizeInTable - 16;

            var valid = new List<int>();
            foreach (int candidate in BaseBlockSizeCandidates)
            {
                if (candidate <= maxSafeBlock)
                    valid.Add(candidate);
            }

            if (valid.Count == 0)
            {
                int fallback = Math.Max(256, maxSafeBlock);
                int half = Math.Max(256, fallback / 2);
                if (half < fallback)
                    valid.Add(half);
                valid.Add(fallback);
            }

            return valid.ToArray();
        }

        // Виправляє гени хромосоми так, щоб BlockSize вміщувався в том і ділився на SplitUnit
        public static GaChromosome Repair(GaChromosome chromosome, int[] validBlockSizes)
        {
            GaChromosome repaired = chromosome.Clone();

            if (repaired.MaxChain < 1)
                repaired.MaxChain = MaxChainCandidates[0];

            if (!Array.Exists(validBlockSizes, s => s == repaired.BlockSize))
                repaired.BlockSize = NearestCandidate(repaired.BlockSize, validBlockSizes);

            if (repaired.SplitUnit <= 0 || repaired.BlockSize % repaired.SplitUnit != 0)
            {
                int chosenUnit = 0;
                for (int i = SplitUnitCandidates.Length - 1; i >= 0; i--)
                {
                    int candidate = SplitUnitCandidates[i];
                    if (candidate <= repaired.BlockSize && repaired.BlockSize % candidate == 0)
                    {
                        chosenUnit = candidate;
                        break;
                    }
                }

                repaired.SplitUnit = chosenUnit > 0 ? chosenUnit : repaired.BlockSize;
            }

            return repaired;
        }

        static int NearestCandidate(int value, int[] candidates)
        {
            int best = candidates[0];
            int bestDiff = Math.Abs(value - best);

            for (int i = 1; i < candidates.Length; i++)
            {
                int diff = Math.Abs(value - candidates[i]);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = candidates[i];
                }
            }

            return best;
        }

        // Генерує випадкову допустиму хромосому
        public static GaChromosome RandomChromosome(Random rnd, int[] validBlockSizes)
        {
            var chromosome = new GaChromosome
            {
                MaxChain = MaxChainCandidates[rnd.Next(MaxChainCandidates.Length)],
                BlockSize = validBlockSizes[rnd.Next(validBlockSizes.Length)],
                SplitUnit = SplitUnitCandidates[rnd.Next(SplitUnitCandidates.Length)],
            };
            return Repair(chromosome, validBlockSizes);
        }

        // Турнірна селекція: обирає найкращу особину з tournamentSize випадкових кандидатів
        public static GaIndividual SelectTournament(
            List<GaIndividual> population,
            int tournamentSize,
            Random rnd
        )
        {
            if (population.Count == 0)
                throw new ArgumentException("популяція порожня");

            int k = Math.Clamp(tournamentSize, 1, population.Count);
            GaIndividual best = population[rnd.Next(population.Count)];

            for (int i = 1; i < k; i++)
            {
                GaIndividual candidate = population[rnd.Next(population.Count)];
                if (CompareIndividuals(candidate, best) < 0)
                    best = candidate;
            }

            return best;
        }

        // Одноточковий кросовер для 3-генної хромосоми
        public static GaChromosome CrossoverSinglePoint(
            GaChromosome parent1,
            GaChromosome parent2,
            Random rnd,
            int[] validBlockSizes
        )
        {
            int cut = rnd.Next(1, 3);
            var child = new GaChromosome
            {
                MaxChain = parent1.MaxChain,
                BlockSize = cut <= 1 ? parent2.BlockSize : parent1.BlockSize,
                SplitUnit = cut <= 2 ? parent2.SplitUnit : parent1.SplitUnit,
            };
            return Repair(child, validBlockSizes);
        }

        // Погенна мутація хромосоми
        public static GaChromosome Mutate(
            GaChromosome chromosome,
            double mutationRate,
            Random rnd,
            int[] validBlockSizes
        )
        {
            GaChromosome mutated = chromosome.Clone();

            if (rnd.NextDouble() < mutationRate)
                mutated.MaxChain = PickOther(mutated.MaxChain, MaxChainCandidates, rnd);

            if (rnd.NextDouble() < mutationRate)
                mutated.BlockSize = PickOther(mutated.BlockSize, validBlockSizes, rnd);

            if (rnd.NextDouble() < mutationRate)
                mutated.SplitUnit = PickOther(mutated.SplitUnit, SplitUnitCandidates, rnd);

            return Repair(mutated, validBlockSizes);
        }

        static int PickOther(int current, int[] candidates, Random rnd)
        {
            if (candidates.Length <= 1)
                return candidates.Length == 1 ? candidates[0] : current;

            int next = candidates[rnd.Next(candidates.Length)];
            if (next == current)
            {
                int idx = Array.IndexOf(candidates, current);
                next = candidates[(Math.Max(0, idx) + 1) % candidates.Length];
            }

            return next;
        }

        // Компаратор для сортування від кращого до гіршого (за Q, далі S і T)
        internal static int CompareIndividuals(GaIndividual a, GaIndividual b)
        {
            int cmpQ = b.Fitness.CompareTo(a.Fitness);
            if (cmpQ != 0)
                return cmpQ;

            int cmpS = b.Evaluation.S.CompareTo(a.Evaluation.S);
            if (cmpS != 0)
                return cmpS;

            return b.Evaluation.T.CompareTo(a.Evaluation.T);
        }

        static GaIndividual EvaluateChromosome(
            List<ArchiveFile> corpus,
            GaChromosome chromosome,
            TeamConfig config,
            string? classifierPath,
            string label
        )
        {
            Settings settings = chromosome.ToSettings(config, classifierPath);
            QEvaluation eval = QualityMetric.Evaluate(corpus, settings, config, label);
            return new GaIndividual(chromosome.Clone(), eval);
        }

        // Базовий генетичний алгоритм: турнірна селекція, одноточковий кросовер, мутація та строгий елітизм
        public static OptimizationResult RunBasicGa(
            List<ArchiveFile> corpus,
            TeamConfig config,
            GaRunOptions? options = null
        )
        {
            options ??= new GaRunOptions { Seed = config.Seed };
            ValidateOptions(options);

            int[] validBlockSizes = ValidBlockSizes(config, corpus);
            var rnd = new Random(options.Seed);
            var totalSw = Stopwatch.StartNew();

            var result = new OptimizationResult { AlgorithmName = "Генетичний алгоритм (базовий)" };
            int evaluations = 0;

            // Покоління 0
            var population = new List<GaIndividual>(options.PopulationSize);
            for (int i = 0; i < options.PopulationSize; i++)
            {
                GaChromosome chromosome = RandomChromosome(rnd, validBlockSizes);
                GaIndividual ind = EvaluateChromosome(
                    corpus,
                    chromosome,
                    config,
                    options.ClassifierPath,
                    $"GA-G0-{i}"
                );
                population.Add(ind);
                result.Evaluated.Add(ind);
                evaluations++;
            }

            population.Sort(CompareIndividuals);
            result.History.Add(BuildGenerationRecord(0, evaluations, population));

            int eliteCount = Math.Clamp(options.ElitismCount, 1, options.PopulationSize - 1);

            // Еволюція поколінь 1 .. Generations
            for (int gen = 1; gen <= options.Generations; gen++)
            {
                var nextPopulation = new List<GaIndividual>(options.PopulationSize);

                // Строгий елітизм: найкращі особини гарантовано переходять без переобчислення (щоб Q не коливався)
                for (int e = 0; e < eliteCount; e++)
                    nextPopulation.Add(population[e]);

                while (nextPopulation.Count < options.PopulationSize)
                {
                    GaIndividual parent1 = SelectTournament(population, options.TournamentSize, rnd);
                    GaIndividual parent2 = SelectTournament(population, options.TournamentSize, rnd);

                    GaChromosome child =
                        rnd.NextDouble() < options.CrossoverRate
                            ? CrossoverSinglePoint(parent1.Chromosome, parent2.Chromosome, rnd, validBlockSizes)
                            : parent1.Chromosome.Clone();

                    child = Mutate(child, options.MutationRate, rnd, validBlockSizes);

                    GaIndividual evaluated = EvaluateChromosome(
                        corpus,
                        child,
                        config,
                        options.ClassifierPath,
                        $"GA-G{gen}-{nextPopulation.Count}"
                    );
                    nextPopulation.Add(evaluated);
                    result.Evaluated.Add(evaluated);
                    evaluations++;
                }

                nextPopulation.Sort(CompareIndividuals);
                population = nextPopulation;
                result.History.Add(BuildGenerationRecord(gen, evaluations, population));
            }

            totalSw.Stop();
            result.Best = population[0];
            result.Evaluations = evaluations;
            result.TotalElapsedSeconds = totalSw.Elapsed.TotalSeconds;

            return result;
        }

        // Випадковий пошук за фіксованої кількості обчислень пристосованості Q
        public static OptimizationResult RunRandomSearch(
            List<ArchiveFile> corpus,
            TeamConfig config,
            int evaluationBudget,
            int seed,
            string? classifierPath = null
        )
        {
            if (evaluationBudget <= 0)
                throw new ArgumentException("кількість обчислень Q має бути додатною");

            int[] validBlockSizes = ValidBlockSizes(config, corpus);
            var rnd = new Random(seed);
            var totalSw = Stopwatch.StartNew();

            var result = new OptimizationResult { AlgorithmName = "Випадковий пошук (Random Search)" };
            GaIndividual? best = null;

            for (int i = 0; i < evaluationBudget; i++)
            {
                GaChromosome chromosome = RandomChromosome(rnd, validBlockSizes);
                GaIndividual candidate = EvaluateChromosome(
                    corpus,
                    chromosome,
                    config,
                    classifierPath,
                    $"Random-{i}"
                );
                result.Evaluated.Add(candidate);

                if (best == null || CompareIndividuals(candidate, best) < 0)
                    best = candidate;
            }

            totalSw.Stop();
            result.Best = best!;
            result.Evaluations = evaluationBudget;
            result.TotalElapsedSeconds = totalSw.Elapsed.TotalSeconds;
            return result;
        }

        // Чесне порівняння ГА та випадкового пошуку за однакової кількості обчислень Q + запис журналу поколінь
        public static GaComparisonReport CompareWithRandomSearch(
            List<ArchiveFile> corpus,
            TeamConfig config,
            GaRunOptions? options = null
        )
        {
            options ??= new GaRunOptions { Seed = config.Seed };

            OptimizationResult ga = RunBasicGa(corpus, config, options);
            OptimizationResult random = RunRandomSearch(
                corpus,
                config,
                ga.Evaluations,
                options.Seed + 1000,
                options.ClassifierPath
            );

            if (!string.IsNullOrWhiteSpace(options.LogPath))
                SaveGenerationsTsv(ga.History, options.LogPath);

            string table = BuildComparisonTable(new[] { ga, random });

            return new GaComparisonReport
            {
                Genetic = ga,
                RandomSearch = random,
                TableText = table,
                GenerationsLogPath = options.LogPath,
            };
        }

        // Зберігає журнал поколінь у TSV-файл (обов'язковий артефакт за п. 11.4 методички)
        public static void SaveGenerationsTsv(List<GaGenerationRecord> history, string filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine(
                "generation\tevaluations\tbest_q\tavg_q\tbest_s\tbest_t\tbest_archive_bytes\tbest_time_s\tbest_params"
            );

            foreach (GaGenerationRecord row in history)
            {
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2:F6}\t{3:F6}\t{4:F6}\t{5:F6}\t{6}\t{7:F4}\t{8}",
                        row.Generation,
                        row.Evaluations,
                        row.BestQ,
                        row.AverageQ,
                        row.BestS,
                        row.BestT,
                        row.BestArchiveBytes,
                        row.BestElapsedSeconds,
                        row.BestParams
                    )
                );
            }

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
        }

        // Таблиця звіту «ГА проти випадкового пошуку» (Пункт 12)
        public static string BuildComparisonTable(IReadOnlyList<OptimizationResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Порівняння методів оптимізації за однакового бюджету обчислень Q");
            sb.AppendLine(
                "Алгоритм\tОбчислень Q\tB_arc (Б)\tЧас арх. t (с)\tS (Стиснення)\tT (Швидкодія)\tНайкраща Q\tСтатус\tНайкращі параметри"
            );

            foreach (OptimizationResult res in results)
            {
                QEvaluation eval = res.Best.Evaluation;
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2}\t{3:F4}\t{4:F4}\t{5:F4}\t{6:F4}\t{7}\t{8}",
                        res.AlgorithmName,
                        res.Evaluations,
                        eval.ArchiveBytes,
                        eval.ElapsedSeconds,
                        eval.S,
                        eval.T,
                        eval.Q,
                        eval.RestoredExact ? "OK" : "ПОМИЛКА",
                        res.Best.Chromosome.Describe()
                    )
                );
            }

            return sb.ToString();
        }

        static GaGenerationRecord BuildGenerationRecord(
            int generation,
            int evaluations,
            List<GaIndividual> sortedPopulation
        )
        {
            double sumQ = 0;
            foreach (GaIndividual ind in sortedPopulation)
                sumQ += ind.Fitness;

            GaIndividual best = sortedPopulation[0];
            return new GaGenerationRecord
            {
                Generation = generation,
                Evaluations = evaluations,
                BestQ = best.Fitness,
                AverageQ = sortedPopulation.Count > 0 ? sumQ / sortedPopulation.Count : 0.0,
                BestS = best.Evaluation.S,
                BestT = best.Evaluation.T,
                BestArchiveBytes = best.Evaluation.ArchiveBytes,
                BestElapsedSeconds = best.Evaluation.ElapsedSeconds,
                BestParams = best.Chromosome.Describe(),
            };
        }

        static void ValidateOptions(GaRunOptions options)
        {
            if (options.PopulationSize < 2)
                throw new ArgumentException("розмір популяції має бути >= 2");
            if (options.Generations < 1)
                throw new ArgumentException("кількість поколінь має бути >= 1");
            if (options.ElitismCount < 1)
                throw new ArgumentException("строгий елітизм вимагає ElitismCount >= 1");
        }
    }
}
