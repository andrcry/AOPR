using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SimpleArchiver
{
    // Параметри багатокритеріальної оптимізації (експертний рівень, Фронт Парето за S та T)
    public class ParetoRunOptions
    {
        public int PopulationSize { get; set; } = 8;
        public int Generations { get; set; } = 4;
        public double CrossoverRate { get; set; } = 0.85;
        public double InitialMutationRate { get; set; } = 0.45;
        public double FinalMutationRate { get; set; } = 0.10;
        public CrossoverType Crossover { get; set; } = CrossoverType.Uniform;
        public bool FullChromosome { get; set; } = true;
        public int Seed { get; set; } = 2001;
        public string OutputPath { get; set; } = "experiments/pareto_front.md";
        public string? ClassifierPath { get; set; }
    }

    // Результат багатокритеріальної оптимізації (множина Парето-оптимальних розв'язків)
    public class ParetoResult
    {
        public List<GaIndividual> ParetoFront { get; set; } = new();
        public List<GaIndividual> AllEvaluated { get; set; } = new();
        public int Evaluations { get; set; }
        public double TotalElapsedSeconds { get; set; }
        public string MarkdownTable { get; set; } = "";
        public string OutputPath { get; set; } = "";
    }

    // Експертний рівень: двокритеріальна оптимізація за (S, T) і побудова фронту Парето за принципом NSGA-II (Пункт 7)
    public static class ParetoOptimizer
    {
        // Перевірка домінування за Парето: A домінує над B, якщо A.S >= B.S && A.T >= B.T і хоча б одна нерівність строга
        public static bool Dominates(double sA, double tA, double sB, double tB) =>
            sA >= sB && tA >= tB && (sA > sB || tA > tB);

        public static bool Dominates(QEvaluation a, QEvaluation b)
        {
            if (!a.RestoredExact && b.RestoredExact)
                return false;
            if (a.RestoredExact && !b.RestoredExact)
                return true;
            if (!a.RestoredExact && !b.RestoredExact)
                return false;

            return Dominates(a.S, a.T, b.S, b.T);
        }

        public static bool Dominates(GaIndividual a, GaIndividual b) =>
            Dominates(a.Evaluation, b.Evaluation);

        // Швидке недоміноване сортування NSGA-II (розбиває множину на фронти F_0, F_1, ...)
        public static List<List<GaIndividual>> FastNonDominatedSort(IReadOnlyList<GaIndividual> population)
        {
            int n = population.Count;
            var fronts = new List<List<GaIndividual>>();
            if (n == 0)
                return fronts;

            var dominatedBy = new List<int>[n];
            var dominationCount = new int[n];
            var firstFrontIndices = new List<int>();

            for (int p = 0; p < n; p++)
                dominatedBy[p] = new List<int>();

            for (int p = 0; p < n; p++)
            {
                for (int q = 0; q < n; q++)
                {
                    if (p == q)
                        continue;

                    if (Dominates(population[p], population[q]))
                        dominatedBy[p].Add(q);
                    else if (Dominates(population[q], population[p]))
                        dominationCount[p]++;
                }

                if (dominationCount[p] == 0)
                    firstFrontIndices.Add(p);
            }

            var currentIndices = firstFrontIndices;
            while (currentIndices.Count > 0)
            {
                var front = new List<GaIndividual>(currentIndices.Count);
                foreach (int idx in currentIndices)
                    front.Add(population[idx]);
                fronts.Add(front);

                var nextIndices = new List<int>();
                foreach (int p in currentIndices)
                {
                    foreach (int q in dominatedBy[p])
                    {
                        dominationCount[q]--;
                        if (dominationCount[q] == 0)
                            nextIndices.Add(q);
                    }
                }

                currentIndices = nextIndices;
            }

            return fronts;
        }

        // Обчислює відстань скупченості (Crowding Distance) для точок одного фронту за (S, T)
        public static double[] ComputeCrowdingDistances(IReadOnlyList<GaIndividual> front)
        {
            int n = front.Count;
            var distances = new double[n];
            if (n == 0)
                return distances;
            if (n <= 2)
            {
                for (int i = 0; i < n; i++)
                    distances[i] = double.PositiveInfinity;
                return distances;
            }

            int[] orderByS = Enumerable.Range(0, n).OrderBy(i => front[i].Evaluation.S).ToArray();
            AssignObjectiveCrowding(front, distances, orderByS, e => e.S);

            int[] orderByT = Enumerable.Range(0, n).OrderBy(i => front[i].Evaluation.T).ToArray();
            AssignObjectiveCrowding(front, distances, orderByT, e => e.T);

            return distances;
        }

        static void AssignObjectiveCrowding(
            IReadOnlyList<GaIndividual> front,
            double[] distances,
            int[] order,
            Func<QEvaluation, double> objective
        )
        {
            distances[order[0]] = double.PositiveInfinity;
            distances[order[^1]] = double.PositiveInfinity;

            double minVal = objective(front[order[0]].Evaluation);
            double maxVal = objective(front[order[^1]].Evaluation);
            double span = maxVal - minVal;
            if (span <= 0)
                return;

            for (int i = 1; i < order.Length - 1; i++)
            {
                if (double.IsPositiveInfinity(distances[order[i]]))
                    continue;

                double prev = objective(front[order[i - 1]].Evaluation);
                double next = objective(front[order[i + 1]].Evaluation);
                distances[order[i]] += (next - prev) / span;
            }
        }

        // Виділяє недоміновану множину розв'язків (Фронт Парето F_0), впорядковану за спаданням S
        public static List<GaIndividual> ExtractParetoFront(IReadOnlyList<GaIndividual> candidates)
        {
            var valid = new List<GaIndividual>();
            foreach (GaIndividual ind in candidates)
            {
                if (ind.Evaluation.RestoredExact)
                    valid.Add(ind);
            }

            if (valid.Count == 0)
                return new List<GaIndividual>();

            List<List<GaIndividual>> fronts = FastNonDominatedSort(valid);
            List<GaIndividual> front = fronts[0];

            // Прибираємо дублікати з однаковою хромосомою та тими самими (S, T)
            var unique = new List<GaIndividual>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GaIndividual ind in front)
            {
                string key = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}|{1:F9}|{2:F9}",
                    ind.Chromosome.GetSignature(true),
                    ind.Evaluation.S,
                    ind.Evaluation.T
                );
                if (seen.Add(key))
                    unique.Add(ind);
            }

            unique.Sort((a, b) =>
            {
                int cmpS = b.Evaluation.S.CompareTo(a.Evaluation.S);
                if (cmpS != 0)
                    return cmpS;
                return b.Evaluation.T.CompareTo(a.Evaluation.T);
            });

            return unique;
        }

        // Багатокритеріальна оптимізація за (S, T) алгоритмом NSGA-II
        public static ParetoResult Optimize(
            List<ArchiveFile> corpus,
            TeamConfig config,
            ParetoRunOptions? options = null
        )
        {
            options ??= new ParetoRunOptions { Seed = config.Seed };
            if (options.PopulationSize < 2)
                throw new ArgumentException("розмір популяції має бути >= 2");
            if (options.Generations < 1)
                throw new ArgumentException("кількість поколінь має бути >= 1");

            int[] validBlockSizes = GeneticOptimizer.ValidBlockSizes(config, corpus);
            var rnd = new Random(options.Seed);
            var sw = Stopwatch.StartNew();

            var allEvaluated = new List<GaIndividual>();
            var seenSignatures = new Dictionary<string, GaIndividual>(StringComparer.Ordinal);
            int evaluations = 0;

            GaIndividual Evaluate(GaChromosome chromosome, string label)
            {
                GaChromosome repaired = GeneticOptimizer.Repair(chromosome, validBlockSizes);
                GaIndividual ind = GeneticOptimizer.EvaluateChromosome(
                    corpus,
                    repaired,
                    config,
                    options.ClassifierPath,
                    label
                );
                allEvaluated.Add(ind);
                evaluations++;

                string sig = repaired.GetSignature(options.FullChromosome);
                if (!seenSignatures.TryGetValue(sig, out GaIndividual? prev) || ind.Fitness > prev.Fitness)
                    seenSignatures[sig] = ind;

                return ind;
            }

            // Покоління 0: граничні орієнтири (макс. швидкість і макс. стиснення) + випадкові хромосоми
            var population = new List<GaIndividual>(options.PopulationSize);
            if (options.FullChromosome && options.PopulationSize >= 4)
            {
                population.Add(
                    Evaluate(
                        new GaChromosome
                        {
                            Parser = ParserKind.Greedy,
                            MaxChain = GeneticOptimizer.MaxChainCandidates[0],
                            BlockSize = validBlockSizes[^1],
                            SplitUnit = 4096,
                            UseDpBlockSplit = false,
                            UsePrediction = false,
                            UseTypeStrategy = options.ClassifierPath != null,
                        },
                        "NSGA2-G0-Fast"
                    )
                );

                population.Add(
                    Evaluate(
                        new GaChromosome
                        {
                            Parser = ParserKind.Dp,
                            MaxChain = 128,
                            BlockSize = validBlockSizes[^1],
                            SplitUnit = 4096,
                            UseDpBlockSplit = true,
                            UsePrediction = true,
                            UseTypeStrategy = options.ClassifierPath != null,
                        },
                        "NSGA2-G0-Dense"
                    )
                );
            }

            while (population.Count < options.PopulationSize)
            {
                GaChromosome rndChrom = GeneticOptimizer.RandomChromosome(
                    rnd,
                    validBlockSizes,
                    options.FullChromosome
                );
                population.Add(Evaluate(rndChrom, $"NSGA2-G0-{population.Count}"));
            }

            // Покоління 1 .. Generations (схема (mu + lambda) NSGA-II)
            for (int gen = 1; gen <= options.Generations; gen++)
            {
                double progress =
                    options.Generations <= 1
                        ? 1.0
                        : Math.Clamp((double)(gen - 1) / (options.Generations - 1), 0.0, 1.0);
                double mutationRate =
                    options.InitialMutationRate
                    - (options.InitialMutationRate - options.FinalMutationRate) * progress;

                AssignRanksAndCrowding(
                    population,
                    out Dictionary<GaIndividual, int> ranks,
                    out Dictionary<GaIndividual, double> crowding
                );

                var offspring = new List<GaIndividual>(options.PopulationSize);
                for (int i = 0; i < options.PopulationSize; i++)
                {
                    GaIndividual p1 = CrowdedTournament(population, ranks, crowding, rnd);
                    GaIndividual p2 = CrowdedTournament(population, ranks, crowding, rnd);

                    GaChromosome child;
                    if (rnd.NextDouble() < options.CrossoverRate)
                    {
                        child =
                            options.Crossover == CrossoverType.Uniform
                                ? GeneticOptimizer.CrossoverUniform(
                                    p1.Chromosome,
                                    p2.Chromosome,
                                    rnd,
                                    validBlockSizes,
                                    options.FullChromosome
                                )
                                : GeneticOptimizer.CrossoverSinglePoint(
                                    p1.Chromosome,
                                    p2.Chromosome,
                                    rnd,
                                    validBlockSizes,
                                    options.FullChromosome
                                );
                    }
                    else
                    {
                        child = p1.Chromosome.Clone();
                    }

                    child = GeneticOptimizer.Mutate(
                        child,
                        mutationRate,
                        rnd,
                        validBlockSizes,
                        options.FullChromosome
                    );
                    offspring.Add(Evaluate(child, $"NSGA2-G{gen}-{i}"));
                }

                var combined = new List<GaIndividual>(population.Count + offspring.Count);
                combined.AddRange(population);
                combined.AddRange(offspring);
                population = SelectNextGenerationNsga2(combined, options.PopulationSize);
            }

            sw.Stop();

            // Формуємо фінальний недомінований фронт Парето з унікальних найкращих представників кожної конфігурації
            List<GaIndividual> paretoFront = ExtractParetoFront(seenSignatures.Values.ToList());
            var result = new ParetoResult
            {
                ParetoFront = paretoFront,
                AllEvaluated = allEvaluated,
                Evaluations = evaluations,
                TotalElapsedSeconds = sw.Elapsed.TotalSeconds,
                OutputPath = options.OutputPath,
            };

            result.MarkdownTable = BuildMarkdownTable(result, config);

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
                SaveMarkdown(result.MarkdownTable, options.OutputPath);

            return result;
        }

        static void AssignRanksAndCrowding(
            IReadOnlyList<GaIndividual> population,
            out Dictionary<GaIndividual, int> ranks,
            out Dictionary<GaIndividual, double> crowding
        )
        {
            ranks = new Dictionary<GaIndividual, int>();
            crowding = new Dictionary<GaIndividual, double>();

            List<List<GaIndividual>> fronts = FastNonDominatedSort(population);
            for (int r = 0; r < fronts.Count; r++)
            {
                List<GaIndividual> front = fronts[r];
                double[] dists = ComputeCrowdingDistances(front);
                for (int i = 0; i < front.Count; i++)
                {
                    ranks[front[i]] = r;
                    crowding[front[i]] = dists[i];
                }
            }
        }

        static GaIndividual CrowdedTournament(
            List<GaIndividual> population,
            Dictionary<GaIndividual, int> ranks,
            Dictionary<GaIndividual, double> crowding,
            Random rnd
        )
        {
            GaIndividual a = population[rnd.Next(population.Count)];
            GaIndividual b = population[rnd.Next(population.Count)];

            int rankA = ranks[a];
            int rankB = ranks[b];
            if (rankA != rankB)
                return rankA < rankB ? a : b;

            return crowding[a] >= crowding[b] ? a : b;
        }

        static List<GaIndividual> SelectNextGenerationNsga2(
            List<GaIndividual> combined,
            int targetSize
        )
        {
            var next = new List<GaIndividual>(targetSize);
            List<List<GaIndividual>> fronts = FastNonDominatedSort(combined);

            foreach (List<GaIndividual> front in fronts)
            {
                if (next.Count + front.Count <= targetSize)
                {
                    next.AddRange(front);
                    continue;
                }

                double[] dists = ComputeCrowdingDistances(front);
                int[] order = Enumerable
                    .Range(0, front.Count)
                    .OrderByDescending(i => dists[i])
                    .ToArray();

                int remaining = targetSize - next.Count;
                for (int i = 0; i < remaining; i++)
                    next.Add(front[order[i]]);
                break;
            }

            return next;
        }

        // Будує Markdown-таблицю фронту Парето за критеріями (S, T)
        public static string BuildMarkdownTable(ParetoResult result, TeamConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Фронт Парето (багатокритеріальна оптимізація за S та T)");
            sb.AppendLine();
            sb.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "- Алгоритм: NSGA-II (оцінок: `{0}`, точок на фронті Парето: `{1}`, час пошуку: `{2:F2}` с)",
                    result.Evaluations,
                    result.ParetoFront.Count,
                    result.TotalElapsedSeconds
                )
            );
            sb.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "- Параметри метрики: `alpha = {0}`, `beta = {1}`, `t_ref = {2} с`, `max_volume_bytes = {3}`",
                    config.Alpha,
                    config.Beta,
                    config.TRef,
                    config.MaxVolumeBytes
                )
            );
            sb.AppendLine();
            sb.AppendLine(
                "| # | Конфігурація (хромосома) | B_arc (Б) | Час t (с) | S (Стиснення) | T (Швидкодія) | Q (довідково) |"
            );
            sb.AppendLine("|---:|---|---:|---:|---:|---:|---:|");

            for (int i = 0; i < result.ParetoFront.Count; i++)
            {
                GaIndividual ind = result.ParetoFront[i];
                QEvaluation eval = ind.Evaluation;
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "| {0} | `{1}` | {2} | {3:F4} | {4:F6} | {5:F6} | {6:F6} |",
                        i + 1,
                        ind.Chromosome.Describe(true),
                        eval.ArchiveBytes,
                        eval.ElapsedSeconds,
                        eval.S,
                        eval.T,
                        eval.Q
                    )
                );
            }

            if (result.ParetoFront.Count > 0)
            {
                GaIndividual bestS = result.ParetoFront.OrderByDescending(x => x.Evaluation.S).First();
                GaIndividual bestT = result.ParetoFront.OrderByDescending(x => x.Evaluation.T).First();
                GaIndividual bestQ = result.ParetoFront.OrderByDescending(x => x.Evaluation.Q).First();

                sb.AppendLine();
                sb.AppendLine("## Ключові точки компромісу");
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "- Максимум стиснення `S`: `{0:F6}` (`T = {1:F6}`, `{2}` байтів) — `{3}`",
                        bestS.Evaluation.S,
                        bestS.Evaluation.T,
                        bestS.Evaluation.ArchiveBytes,
                        bestS.Chromosome.Describe(true)
                    )
                );
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "- Максимум швидкодії `T`: `{0:F6}` (`S = {1:F6}`, `{2:F4}` с) — `{3}`",
                        bestT.Evaluation.T,
                        bestT.Evaluation.S,
                        bestT.Evaluation.ElapsedSeconds,
                        bestT.Chromosome.Describe(true)
                    )
                );
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "- Максимум зваженої метрики `Q`: `{0:F6}` (`S = {1:F6}`, `T = {2:F6}`) — `{3}`",
                        bestQ.Evaluation.Q,
                        bestQ.Evaluation.S,
                        bestQ.Evaluation.T,
                        bestQ.Chromosome.Describe(true)
                    )
                );
            }

            return sb.ToString();
        }

        public static void SaveMarkdown(string markdown, string filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, markdown, new UTF8Encoding(false));
        }
    }
}
