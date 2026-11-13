using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SimpleArchiver
{
    // Спосіб кросоверу для генетичного алгоритму
    public enum CrossoverType
    {
        SinglePoint,
        Uniform,
    }

    // Хромосома для оптимізації параметрів архіватора:
    // базові 3 гени (MaxChain, BlockSize, SplitUnit) + розширення (Parser, UseDpBlockSplit, UsePrediction, UseTypeStrategy)
    public class GaChromosome
    {
        // Базові гени
        public int MaxChain { get; set; } = 64;
        public int BlockSize { get; set; } = Settings.teamBlockSize;
        public int SplitUnit { get; set; } = 2048;

        // Просунуті гени повної хромосоми
        public ParserKind Parser { get; set; } = ParserKind.Lazy;
        public bool UseDpBlockSplit { get; set; } = false;
        public bool UsePrediction { get; set; } = false;
        public bool UseTypeStrategy { get; set; } = false;

        public GaChromosome Clone() =>
            new GaChromosome
            {
                MaxChain = MaxChain,
                BlockSize = BlockSize,
                SplitUnit = SplitUnit,
                Parser = Parser,
                UseDpBlockSplit = UseDpBlockSplit,
                UsePrediction = UsePrediction,
                UseTypeStrategy = UseTypeStrategy,
            };

        // Перетворює хромосому на налаштування архіватора з урахуванням team_config.json
        public Settings ToSettings(TeamConfig config, string? classifierPath = null)
        {
            TypeStrategy[] strategies = TypeStrategy.Defaults();
            foreach (TypeStrategy strategy in strategies)
            {
                strategy.BlockSize = Math.Min(strategy.BlockSize, BlockSize);
                if (strategy.BlockSize <= 0 || strategy.BlockSize % SplitUnit != 0)
                    strategy.BlockSize = BlockSize;
            }

            return new Settings
            {
                MaxChain = MaxChain,
                BlockSize = BlockSize,
                SplitUnit = SplitUnit,
                Parser = Parser,
                UseDpBlockSplit = UseDpBlockSplit,
                UsePrediction = UsePrediction,
                UseTypeStrategy = UseTypeStrategy,
                TypeStrategies = strategies,
                MaxVolumeBytes = config.MaxVolumeBytes,
                TypePriorities = (int[])config.TypePriorities.Clone(),
                ClassifierPath = classifierPath,
            };
        }

        public string GetSignature(bool full = true)
        {
            if (!full)
                return $"{MaxChain}|{BlockSize}|{SplitUnit}";

            return $"{MaxChain}|{BlockSize}|{SplitUnit}|{(int)Parser}|{(UseDpBlockSplit ? 1 : 0)}|{(UsePrediction ? 1 : 0)}|{(UseTypeStrategy ? 1 : 0)}";
        }

        public string Describe(bool full = false)
        {
            if (!full && Parser == ParserKind.Lazy && !UseDpBlockSplit && !UsePrediction && !UseTypeStrategy)
                return $"MaxChain={MaxChain}, BlockSize={BlockSize}, SplitUnit={SplitUnit}";

            return $"Parser={Parser}, MaxChain={MaxChain}, BlockSize={BlockSize}, SplitUnit={SplitUnit}, "
                + $"DpSplit={(UseDpBlockSplit ? "on" : "off")}, Predict={(UsePrediction ? "on" : "off")}, Strategy={(UseTypeStrategy ? "on" : "off")}";
        }
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
        public double MutationRate { get; set; }
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
        public bool AdaptiveMutation { get; set; } = false;
        public double InitialMutationRate { get; set; } = 0.45;
        public double FinalMutationRate { get; set; } = 0.08;
        public CrossoverType Crossover { get; set; } = CrossoverType.SinglePoint;
        public bool Advanced { get; set; } = false;
        public int ElitismCount { get; set; } = 1;
        public int Seed { get; set; } = 2001;
        public string LogPath { get; set; } = "experiments/ga_generations.tsv";
        public string? ClassifierPath { get; set; }
    }

    // Результат одного методу оптимізації (ГА, випадкового або локального пошуку)
    public class OptimizationResult
    {
        public string AlgorithmName { get; set; } = "";
        public GaIndividual Best { get; set; } = null!;
        public int Evaluations { get; set; }
        public double TotalElapsedSeconds { get; set; }
        public List<GaGenerationRecord> History { get; set; } = new();
        public List<GaIndividual> Evaluated { get; set; } = new();
    }

    // Звіт порівняння ГА, випадкового пошуку та локального пошуку за однакового бюджету Q
    public class GaComparisonReport
    {
        public OptimizationResult Genetic { get; set; } = null!;
        public OptimizationResult RandomSearch { get; set; } = null!;
        public OptimizationResult? LocalSearch { get; set; }
        public string TableText { get; set; } = "";
        public string GenerationsLogPath { get; set; } = "";
    }

    // Блок оптимізації параметрів архіватора (базовий та просунутий рівні)
    public static class GeneticOptimizer
    {
        public static readonly int[] MaxChainCandidates = { 4, 8, 16, 32, 64, 128, 256 };
        public static readonly int[] BaseBlockSizeCandidates = { 2048, 4096, 8192, 16384, 32768, 65536 };
        public static readonly int[] SplitUnitCandidates = { 512, 1024, 2048, 4096 };
        public static readonly ParserKind[] ParserCandidates =
        {
            ParserKind.Greedy,
            ParserKind.Lazy,
            ParserKind.Dp,
        };

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

        // Виправляє гени хромосоми так, щоб BlockSize вміщувався в том, ділився на SplitUnit і ДП-розбиття мало обмежене розгалуження
        public static GaChromosome Repair(GaChromosome chromosome, int[] validBlockSizes)
        {
            GaChromosome repaired = chromosome.Clone();

            if (repaired.MaxChain < 1)
                repaired.MaxChain = MaxChainCandidates[0];

            if (!Array.Exists(validBlockSizes, s => s == repaired.BlockSize))
                repaired.BlockSize = NearestCandidate(repaired.BlockSize, validBlockSizes);

            int minUnitForDp = repaired.UseDpBlockSplit ? Math.Max(512, repaired.BlockSize / 8) : 1;
            if (
                repaired.SplitUnit <= 0
                || repaired.BlockSize % repaired.SplitUnit != 0
                || repaired.SplitUnit < minUnitForDp
            )
            {
                int chosenUnit = 0;
                for (int i = SplitUnitCandidates.Length - 1; i >= 0; i--)
                {
                    int candidate = SplitUnitCandidates[i];
                    if (
                        candidate <= repaired.BlockSize
                        && repaired.BlockSize % candidate == 0
                        && candidate >= minUnitForDp
                    )
                    {
                        chosenUnit = candidate;
                        break;
                    }
                }

                if (chosenUnit == 0)
                {
                    for (int i = SplitUnitCandidates.Length - 1; i >= 0; i--)
                    {
                        int candidate = SplitUnitCandidates[i];
                        if (candidate <= repaired.BlockSize && repaired.BlockSize % candidate == 0)
                        {
                            chosenUnit = candidate;
                            break;
                        }
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

        // Генерує випадкову допустиму хромосому (базову 3-генну або повну 7-генну)
        public static GaChromosome RandomChromosome(
            Random rnd,
            int[] validBlockSizes,
            bool fullChromosome = false
        )
        {
            var chromosome = new GaChromosome
            {
                MaxChain = MaxChainCandidates[rnd.Next(MaxChainCandidates.Length)],
                BlockSize = validBlockSizes[rnd.Next(validBlockSizes.Length)],
                SplitUnit = SplitUnitCandidates[rnd.Next(SplitUnitCandidates.Length)],
            };

            if (fullChromosome)
            {
                chromosome.Parser = ParserCandidates[rnd.Next(ParserCandidates.Length)];
                chromosome.UseDpBlockSplit = rnd.Next(2) == 1;
                chromosome.UsePrediction = rnd.Next(2) == 1;
                chromosome.UseTypeStrategy = rnd.Next(2) == 1;
            }

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

        // Одноточковий кросовер (підтримує 3-генну та повну 7-генну хромосоми)
        public static GaChromosome CrossoverSinglePoint(
            GaChromosome parent1,
            GaChromosome parent2,
            Random rnd,
            int[] validBlockSizes,
            bool fullChromosome = false
        )
        {
            int geneCount = fullChromosome ? 7 : 3;
            int cut = rnd.Next(1, geneCount);

            var child = new GaChromosome
            {
                MaxChain = parent1.MaxChain,
                BlockSize = cut <= 1 ? parent2.BlockSize : parent1.BlockSize,
                SplitUnit = cut <= 2 ? parent2.SplitUnit : parent1.SplitUnit,
                Parser =
                    fullChromosome && cut <= 3 ? parent2.Parser : parent1.Parser,
                UseDpBlockSplit =
                    fullChromosome && cut <= 4 ? parent2.UseDpBlockSplit : parent1.UseDpBlockSplit,
                UsePrediction =
                    fullChromosome && cut <= 5 ? parent2.UsePrediction : parent1.UsePrediction,
                UseTypeStrategy =
                    fullChromosome && cut <= 6 ? parent2.UseTypeStrategy : parent1.UseTypeStrategy,
            };

            return Repair(child, validBlockSizes);
        }

        // Другий тип кросоверу: рівномірний кросовер (Uniform) для незалежного обміну генами
        public static GaChromosome CrossoverUniform(
            GaChromosome parent1,
            GaChromosome parent2,
            Random rnd,
            int[] validBlockSizes,
            bool fullChromosome = false
        )
        {
            var child = new GaChromosome
            {
                MaxChain = rnd.Next(2) == 0 ? parent1.MaxChain : parent2.MaxChain,
                BlockSize = rnd.Next(2) == 0 ? parent1.BlockSize : parent2.BlockSize,
                SplitUnit = rnd.Next(2) == 0 ? parent1.SplitUnit : parent2.SplitUnit,
                Parser = fullChromosome
                    ? (rnd.Next(2) == 0 ? parent1.Parser : parent2.Parser)
                    : parent1.Parser,
                UseDpBlockSplit = fullChromosome
                    ? (rnd.Next(2) == 0 ? parent1.UseDpBlockSplit : parent2.UseDpBlockSplit)
                    : parent1.UseDpBlockSplit,
                UsePrediction = fullChromosome
                    ? (rnd.Next(2) == 0 ? parent1.UsePrediction : parent2.UsePrediction)
                    : parent1.UsePrediction,
                UseTypeStrategy = fullChromosome
                    ? (rnd.Next(2) == 0 ? parent1.UseTypeStrategy : parent2.UseTypeStrategy)
                    : parent1.UseTypeStrategy,
            };

            return Repair(child, validBlockSizes);
        }

        // Погенна мутація хромосоми
        public static GaChromosome Mutate(
            GaChromosome chromosome,
            double mutationRate,
            Random rnd,
            int[] validBlockSizes,
            bool fullChromosome = false
        )
        {
            GaChromosome mutated = chromosome.Clone();

            if (rnd.NextDouble() < mutationRate)
                mutated.MaxChain = PickOther(mutated.MaxChain, MaxChainCandidates, rnd);

            if (rnd.NextDouble() < mutationRate)
                mutated.BlockSize = PickOther(mutated.BlockSize, validBlockSizes, rnd);

            if (rnd.NextDouble() < mutationRate)
                mutated.SplitUnit = PickOther(mutated.SplitUnit, SplitUnitCandidates, rnd);

            if (fullChromosome)
            {
                if (rnd.NextDouble() < mutationRate)
                    mutated.Parser = PickOther(mutated.Parser, ParserCandidates, rnd);

                if (rnd.NextDouble() < mutationRate)
                    mutated.UseDpBlockSplit = !mutated.UseDpBlockSplit;

                if (rnd.NextDouble() < mutationRate)
                    mutated.UsePrediction = !mutated.UsePrediction;

                if (rnd.NextDouble() < mutationRate)
                    mutated.UseTypeStrategy = !mutated.UseTypeStrategy;
            }

            return Repair(mutated, validBlockSizes);
        }

        // Адаптивна ймовірність мутації: вища на перших поколіннях і поступово спадає до останнього
        public static double ComputeMutationRate(int generation, GaRunOptions options)
        {
            if (!options.AdaptiveMutation)
                return options.MutationRate;

            if (generation <= 0)
                return options.InitialMutationRate;

            if (options.Generations <= 1)
                return options.FinalMutationRate;

            double progress = Math.Clamp((double)(generation - 1) / (options.Generations - 1), 0.0, 1.0);
            return options.InitialMutationRate - (options.InitialMutationRate - options.FinalMutationRate) * progress;
        }

        static T PickOther<T>(T current, T[] candidates, Random rnd) where T : notnull
        {
            if (candidates.Length <= 1)
                return candidates.Length == 1 ? candidates[0] : current;

            T next = candidates[rnd.Next(candidates.Length)];
            if (EqualityComparer<T>.Default.Equals(next, current))
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

        internal static GaIndividual EvaluateChromosome(
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

        // Базовий генетичний алгоритм (3 гени, одноточковий кросовер, фіксована мутація, строгий елітизм)
        public static OptimizationResult RunBasicGa(
            List<ArchiveFile> corpus,
            TeamConfig config,
            GaRunOptions? options = null
        )
        {
            options ??= new GaRunOptions { Seed = config.Seed };
            var basicOptions = new GaRunOptions
            {
                PopulationSize = options.PopulationSize,
                Generations = options.Generations,
                TournamentSize = options.TournamentSize,
                CrossoverRate = options.CrossoverRate,
                MutationRate = options.MutationRate,
                AdaptiveMutation = false,
                Crossover = CrossoverType.SinglePoint,
                Advanced = false,
                ElitismCount = options.ElitismCount,
                Seed = options.Seed,
                LogPath = options.LogPath,
                ClassifierPath = options.ClassifierPath,
            };
            return RunGa(corpus, config, basicOptions);
        }

        // Просунутий генетичний алгоритм (повна 7-генна хромосома, Uniform або SinglePoint кросовер, адаптивна мутація, елітизм)
        public static OptimizationResult RunAdvancedGa(
            List<ArchiveFile> corpus,
            TeamConfig config,
            GaRunOptions? options = null
        )
        {
            options ??= new GaRunOptions
            {
                Seed = config.Seed,
                Advanced = true,
                AdaptiveMutation = true,
                Crossover = CrossoverType.Uniform,
            };

            var advOptions = new GaRunOptions
            {
                PopulationSize = options.PopulationSize,
                Generations = options.Generations,
                TournamentSize = options.TournamentSize,
                CrossoverRate = options.CrossoverRate,
                MutationRate = options.MutationRate,
                AdaptiveMutation = true,
                InitialMutationRate = options.InitialMutationRate,
                FinalMutationRate = options.FinalMutationRate,
                Crossover = options.Crossover,
                Advanced = true,
                ElitismCount = options.ElitismCount,
                Seed = options.Seed,
                LogPath = options.LogPath,
                ClassifierPath = options.ClassifierPath,
            };
            return RunGa(corpus, config, advOptions);
        }

        // Загальний цикл генетичного алгоритму (підтримує базовий та просунутий режими)
        public static OptimizationResult RunGa(
            List<ArchiveFile> corpus,
            TeamConfig config,
            GaRunOptions options
        )
        {
            ValidateOptions(options);

            int[] validBlockSizes = ValidBlockSizes(config, corpus);
            var rnd = new Random(options.Seed);
            var totalSw = Stopwatch.StartNew();

            string name = options.Advanced
                ? $"Генетичний алгоритм (просунутий, {options.Crossover})"
                : "Генетичний алгоритм (базовий)";
            var result = new OptimizationResult { AlgorithmName = name };
            int evaluations = 0;

            // Покоління 0
            var population = new List<GaIndividual>(options.PopulationSize);
            for (int i = 0; i < options.PopulationSize; i++)
            {
                GaChromosome chromosome = RandomChromosome(rnd, validBlockSizes, options.Advanced);
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
            result.History.Add(
                BuildGenerationRecord(
                    0,
                    evaluations,
                    ComputeMutationRate(0, options),
                    population,
                    options.Advanced
                )
            );

            int eliteCount = Math.Clamp(options.ElitismCount, 1, options.PopulationSize - 1);

            // Еволюція поколінь 1 .. Generations
            for (int gen = 1; gen <= options.Generations; gen++)
            {
                double mutationRate = ComputeMutationRate(gen, options);
                var nextPopulation = new List<GaIndividual>(options.PopulationSize);

                // Строгий елітизм: найкращі особини гарантовано переходять без переобчислення
                for (int e = 0; e < eliteCount; e++)
                    nextPopulation.Add(population[e]);

                while (nextPopulation.Count < options.PopulationSize)
                {
                    GaIndividual parent1 = SelectTournament(population, options.TournamentSize, rnd);
                    GaIndividual parent2 = SelectTournament(population, options.TournamentSize, rnd);

                    GaChromosome child;
                    if (rnd.NextDouble() < options.CrossoverRate)
                    {
                        child =
                            options.Crossover == CrossoverType.Uniform
                                ? CrossoverUniform(
                                    parent1.Chromosome,
                                    parent2.Chromosome,
                                    rnd,
                                    validBlockSizes,
                                    options.Advanced
                                )
                                : CrossoverSinglePoint(
                                    parent1.Chromosome,
                                    parent2.Chromosome,
                                    rnd,
                                    validBlockSizes,
                                    options.Advanced
                                );
                    }
                    else
                    {
                        child = parent1.Chromosome.Clone();
                    }

                    child = Mutate(child, mutationRate, rnd, validBlockSizes, options.Advanced);

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
                result.History.Add(
                    BuildGenerationRecord(gen, evaluations, mutationRate, population, options.Advanced)
                );
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
            string? classifierPath = null,
            bool fullChromosome = false
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
                GaChromosome chromosome = RandomChromosome(rnd, validBlockSizes, fullChromosome);
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

        // Генерує сусідні стани хромосоми для локального пошуку (Hill Climbing)
        public static List<GaChromosome> GetNeighbors(
            GaChromosome current,
            int[] validBlockSizes,
            bool fullChromosome = true
        )
        {
            var neighbors = new List<GaChromosome>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { current.GetSignature(fullChromosome) };

            void TryAdd(GaChromosome candidate)
            {
                GaChromosome repaired = Repair(candidate, validBlockSizes);
                string sig = repaired.GetSignature(fullChromosome);
                if (seen.Add(sig))
                    neighbors.Add(repaired);
            }

            // Сусіди за MaxChain
            int chainIdx = Array.IndexOf(MaxChainCandidates, current.MaxChain);
            if (chainIdx > 0)
            {
                var c = current.Clone();
                c.MaxChain = MaxChainCandidates[chainIdx - 1];
                TryAdd(c);
            }
            if (chainIdx >= 0 && chainIdx + 1 < MaxChainCandidates.Length)
            {
                var c = current.Clone();
                c.MaxChain = MaxChainCandidates[chainIdx + 1];
                TryAdd(c);
            }

            // Сусіди за BlockSize
            int blockIdx = Array.IndexOf(validBlockSizes, current.BlockSize);
            if (blockIdx > 0)
            {
                var c = current.Clone();
                c.BlockSize = validBlockSizes[blockIdx - 1];
                TryAdd(c);
            }
            if (blockIdx >= 0 && blockIdx + 1 < validBlockSizes.Length)
            {
                var c = current.Clone();
                c.BlockSize = validBlockSizes[blockIdx + 1];
                TryAdd(c);
            }

            // Сусіди за SplitUnit
            int unitIdx = Array.IndexOf(SplitUnitCandidates, current.SplitUnit);
            if (unitIdx > 0)
            {
                var c = current.Clone();
                c.SplitUnit = SplitUnitCandidates[unitIdx - 1];
                TryAdd(c);
            }
            if (unitIdx >= 0 && unitIdx + 1 < SplitUnitCandidates.Length)
            {
                var c = current.Clone();
                c.SplitUnit = SplitUnitCandidates[unitIdx + 1];
                TryAdd(c);
            }

            if (fullChromosome)
            {
                foreach (ParserKind kind in ParserCandidates)
                {
                    if (kind != current.Parser)
                    {
                        var c = current.Clone();
                        c.Parser = kind;
                        TryAdd(c);
                    }
                }

                var dpFlip = current.Clone();
                dpFlip.UseDpBlockSplit = !dpFlip.UseDpBlockSplit;
                TryAdd(dpFlip);

                var predFlip = current.Clone();
                predFlip.UsePrediction = !predFlip.UsePrediction;
                TryAdd(predFlip);

                var stratFlip = current.Clone();
                stratFlip.UseTypeStrategy = !stratFlip.UseTypeStrategy;
                TryAdd(stratFlip);
            }

            return neighbors;
        }

        // Локальний пошук (Hill Climbing із перезапуском із випадкової точки на плато) за рівного бюджету Q
        public static OptimizationResult RunLocalSearch(
            List<ArchiveFile> corpus,
            TeamConfig config,
            int evaluationBudget,
            int seed,
            string? classifierPath = null,
            bool fullChromosome = true
        )
        {
            if (evaluationBudget <= 0)
                throw new ArgumentException("кількість обчислень Q має бути додатною");

            int[] validBlockSizes = ValidBlockSizes(config, corpus);
            var rnd = new Random(seed);
            var totalSw = Stopwatch.StartNew();

            var result = new OptimizationResult
            {
                AlgorithmName = "Локальний пошук (Hill Climbing)",
            };
            var visited = new HashSet<string>(StringComparer.Ordinal);
            int evaluations = 0;

            GaChromosome start = RandomChromosome(rnd, validBlockSizes, fullChromosome);
            visited.Add(start.GetSignature(fullChromosome));
            GaIndividual current = EvaluateChromosome(
                corpus,
                start,
                config,
                classifierPath,
                "HC-0"
            );
            result.Evaluated.Add(current);
            evaluations++;
            GaIndividual globalBest = current;

            while (evaluations < evaluationBudget)
            {
                List<GaChromosome> neighbors = GetNeighbors(
                    current.Chromosome,
                    validBlockSizes,
                    fullChromosome
                );

                // Перемішуємо сусідів і відкидаємо вже перевірені стани
                for (int i = neighbors.Count - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    (neighbors[i], neighbors[j]) = (neighbors[j], neighbors[i]);
                }

                GaIndividual? bestImprovingNeighbor = null;
                foreach (GaChromosome neighbor in neighbors)
                {
                    if (evaluations >= evaluationBudget)
                        break;

                    string sig = neighbor.GetSignature(fullChromosome);
                    if (!visited.Add(sig))
                        continue;

                    GaIndividual candidate = EvaluateChromosome(
                        corpus,
                        neighbor,
                        config,
                        classifierPath,
                        $"HC-{evaluations}"
                    );
                    result.Evaluated.Add(candidate);
                    evaluations++;

                    if (CompareIndividuals(candidate, globalBest) < 0)
                        globalBest = candidate;

                    if (
                        CompareIndividuals(candidate, current) < 0
                        && (
                            bestImprovingNeighbor == null
                            || CompareIndividuals(candidate, bestImprovingNeighbor) < 0
                        )
                    )
                    {
                        bestImprovingNeighbor = candidate;
                    }
                }

                if (bestImprovingNeighbor != null)
                {
                    current = bestImprovingNeighbor;
                }
                else if (evaluations < evaluationBudget)
                {
                    // Досягнуто локального оптимуму: перезапуск з іншого неперевіреного стану
                    GaChromosome restart = RandomChromosome(rnd, validBlockSizes, fullChromosome);
                    for (int attempt = 0; attempt < 25; attempt++)
                    {
                        if (visited.Add(restart.GetSignature(fullChromosome)))
                            break;
                        restart = RandomChromosome(rnd, validBlockSizes, fullChromosome);
                    }

                    current = EvaluateChromosome(
                        corpus,
                        restart,
                        config,
                        classifierPath,
                        $"HC-R{evaluations}"
                    );
                    result.Evaluated.Add(current);
                    evaluations++;

                    if (CompareIndividuals(current, globalBest) < 0)
                        globalBest = current;
                }
            }

            totalSw.Stop();
            result.Best = globalBest;
            result.Evaluations = evaluations;
            result.TotalElapsedSeconds = totalSw.Elapsed.TotalSeconds;
            return result;
        }

        // Чесне порівняння базового ГА та випадкового пошуку за однакової кількості обчислень Q
        public static GaComparisonReport CompareWithRandomSearch(
            List<ArchiveFile> corpus,
            TeamConfig config,
            GaRunOptions? options = null
        )
        {
            options ??= new GaRunOptions { Seed = config.Seed };

            OptimizationResult ga = options.Advanced
                ? RunAdvancedGa(corpus, config, options)
                : RunBasicGa(corpus, config, options);

            OptimizationResult random = RunRandomSearch(
                corpus,
                config,
                ga.Evaluations,
                options.Seed + 1000,
                options.ClassifierPath,
                options.Advanced
            );

            OptimizationResult? local = options.Advanced
                ? RunLocalSearch(
                    corpus,
                    config,
                    ga.Evaluations,
                    options.Seed + 2000,
                    options.ClassifierPath,
                    true
                )
                : null;

            if (!string.IsNullOrWhiteSpace(options.LogPath))
                SaveGenerationsTsv(ga.History, options.LogPath);

            string table = local == null
                ? BuildComparisonTable(new[] { ga, random }, options.Advanced)
                : BuildComparisonTable(new[] { ga, random, local }, options.Advanced);

            return new GaComparisonReport
            {
                Genetic = ga,
                RandomSearch = random,
                LocalSearch = local,
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
                "generation\tevaluations\tbest_q\tavg_q\tbest_s\tbest_t\tbest_archive_bytes\tbest_time_s\tmutation_rate\tbest_params"
            );

            foreach (GaGenerationRecord row in history)
            {
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2:F6}\t{3:F6}\t{4:F6}\t{5:F6}\t{6}\t{7:F4}\t{8:F4}\t{9}",
                        row.Generation,
                        row.Evaluations,
                        row.BestQ,
                        row.AverageQ,
                        row.BestS,
                        row.BestT,
                        row.BestArchiveBytes,
                        row.BestElapsedSeconds,
                        row.MutationRate,
                        row.BestParams
                    )
                );
            }

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
        }

        // Таблиця звіту порівняння методів оптимізації за однакового бюджету Q
        public static string BuildComparisonTable(
            IReadOnlyList<OptimizationResult> results,
            bool fullChromosome = false
        )
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
                        res.Best.Chromosome.Describe(fullChromosome)
                    )
                );
            }

            return sb.ToString();
        }

        static GaGenerationRecord BuildGenerationRecord(
            int generation,
            int evaluations,
            double mutationRate,
            List<GaIndividual> sortedPopulation,
            bool fullChromosome
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
                MutationRate = mutationRate,
                BestParams = best.Chromosome.Describe(fullChromosome),
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
