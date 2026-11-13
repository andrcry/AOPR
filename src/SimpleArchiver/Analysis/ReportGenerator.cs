using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SimpleArchiver
{
    // Команда report: автоматичний вимір Q і генерація всіх таблиць звіту в один файл
    // experiments/FULL_REPORT_TABLES.md (розділ 12 методички + додаткові таблиці).
    //
    // Що робить по кроках:
    //   0. параметри команди і середовище виконання (час t залежить від комп'ютера);
    //   1. еталонні і зіпсовані архіви викладача;
    //   2. навчання класифікатора за протоколом, macro-F1 і матриця помилок;
    //   3. прогін пакування/розпакування всього корпусу в кількох режимах, лог experiments/corpus_run.log;
    //   4. обов'язкові таблиці 1-5: розбір LZ77, вплив блоків, рюкзак, порядок операцій, ГА проти випадкового пошуку;
    //   5. додаткові таблиці: ентропія залишків прогнозу, витік метаданих.
    // Опції: --quick (без ГА, лише вже збережений журнал), --ga-pop N, --ga-gen N
    public static class ReportGenerator
    {
        public const string ReportPath = "experiments/FULL_REPORT_TABLES.md";
        public const string CorpusLogPath = "experiments/corpus_run.log";
        public const string GaLogPath = "experiments/ga_generations.tsv";
        public const string GaAdvancedLogPath = "experiments/ga_generations_advanced.tsv";
        public const string ModelPath = "experiments/classifier.model"; // навчений класифікатор для --model

        static readonly CultureInfo c = CultureInfo.InvariantCulture;

        public static void Run(string[] args)
        {
            bool quick = false;
            int gaPopulation = 10;
            int gaGenerations = 15;
            int index = 1;

            while (index < args.Length && args[index].StartsWith("--"))
            {
                string option = args[index];
                if (option == "--quick")
                {
                    quick = true;
                    index++;
                    continue;
                }

                if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value) || value <= 0)
                    throw new ArgumentException($"після {option} потрібне додатне ціле число");

                if (option == "--ga-pop")
                    gaPopulation = value;
                else if (option == "--ga-gen")
                    gaGenerations = value;
                else
                    throw new ArgumentException($"невідома опція {option}");
                index += 2;
            }

            string corpusDir = index < args.Length ? args[index] : "data/corpus";
            if (!Directory.Exists(corpusDir))
                throw new ArgumentException($"папку корпусу не знайдено: {corpusDir}");

            Directory.CreateDirectory("experiments");
            Stopwatch total = Stopwatch.StartNew();
            TeamConfig config = TeamConfig.LoadIfExists(TeamConfig.DefaultPath);
            List<ArchiveFile> corpus = GeneticOptimizer.LoadCorpus(new[] { corpusDir });
            var md = new StringBuilder();

            md.AppendLine("# Таблиці звіту (згенеровано командою `report`)");
            md.AppendLine();
            md.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm}. Корпус: `{corpusDir}`, {corpus.Count} файлів, {corpus.Sum(f => (long)f.Data.Length)} байтів.");
            md.AppendLine();

            Step("0. Параметри і середовище");
            Section0(md, config);

            Step("1. Еталонні архіви");
            Section1(md);

            Step("2. Класифікатор");
            string? model = Section2(md, config);

            Step("3. Прогін корпусу (Q у різних режимах)");
            Section3(md, corpus, config, model);

            Step("4. Таблиця 1: розбір LZ77");
            md.AppendLine("## Таблиця 1. Розбір потоку: жадібний, лінивий і ДП-розбір");
            md.AppendLine();
            AppendTables(md, MeasureTable.Build(new[] { "measure", corpusDir }, 1));

            Step("5. Таблиця 2: вплив блоків на Q");
            md.AppendLine("## Таблиця 2. Вплив кожного блока на Q");
            md.AppendLine();
            md.AppendLine("Кожен блок вимикається окремо, решта - як у повній системі.");
            md.AppendLine();
            AppendTables(md, BlockImpactTable.Build(corpus, config, model, CorpusLogPath));

            Step("6. Таблиця 3: рюкзак");
            md.AppendLine("## Таблиця 3. ДП проти жадібного алгоритму на пакуванні першого тому");
            md.AppendLine();
            var knapsackArgs = new List<string> { "knapsack-table" };
            if (model != null)
                knapsackArgs.AddRange(new[] { "--model", model });
            knapsackArgs.Add(corpusDir);
            AppendTables(md, KnapsackTable.Build(knapsackArgs.ToArray(), 1));

            Step("7. Таблиця 4: порядок операцій");
            md.AppendLine("## Таблиця 4. Порядок операцій: стиснути -> зашифрувати проти зворотного");
            md.AppendLine();
            AppendTables(md, CryptoOrderTable.Build(new[] { "crypto-order", corpusDir }, 1));

            Step("8. Таблиця 5: ГА проти випадкового пошуку");
            Section5(md, corpus, config, model, quick, gaPopulation, gaGenerations);

            Step("9. Додаткові таблиці");
            md.AppendLine("## Додатково. Ентропія залишків прогнозу байта");
            md.AppendLine();
            AppendTables(md, EntropyTable.Build(new[] { "entropy-table", corpusDir }, 1));
            md.AppendLine("## Додатково. Витік метаданих і вартість вирівнювання");
            md.AppendLine();
            AppendTables(md, LeakTable.Build(new[] { "leak-table", "--pad", "4096", corpusDir }, 1));

            if (File.Exists("experiments/pareto_front.md"))
            {
                md.AppendLine("## Додатково. Фронт Парето");
                md.AppendLine();
                md.AppendLine("Див. `experiments/pareto_front.md` (команда `pareto`).");
                md.AppendLine();
            }

            md.AppendLine("---");
            md.AppendLine($"Звіт згенеровано за {total.Elapsed.TotalSeconds.ToString("F1", c)} с.");

            File.WriteAllText(ReportPath, md.ToString());
            Console.WriteLine($"Готово: {ReportPath}, лог прогонів: {CorpusLogPath}");
        }

        static void Step(string name) => Console.WriteLine($"[report] {name}...");

        // 0. Параметри з team_config.json і середовище, в якому виміряно час
        static void Section0(StringBuilder md, TeamConfig config)
        {
            md.AppendLine("## 0. Параметри команди і середовище виконання");
            md.AppendLine();
            foreach (string line in config.Describe())
                md.AppendLine($"- {line}");
            md.AppendLine($"- ОС: {RuntimeInformation.OSDescription}, .NET {Environment.Version}, процесорів: {Environment.ProcessorCount}");
            md.AppendLine("- Час t і T залежать від комп'ютера. Офіційний вимір робить викладач у своєму середовищі.");
            md.AppendLine();
        }

        // 1. Еталонні архіви і очікувані коди помилок
        static void Section1(StringBuilder md)
        {
            md.AppendLine("## 1. Еталонні і зіпсовані архіви викладача");
            md.AppendLine();
            if (!Directory.Exists("data/reference"))
            {
                md.AppendLine("Папки `data/reference` немає.");
                md.AppendLine();
                return;
            }

            // Останній рядок таблиці ReferenceCheck - підсумок "Пройдено: N з M"
            AppendTables(md, ReferenceCheck.Build("data/reference", out _, out _));
        }

        // 2. Класифікатор: навчання за протоколом, macro-F1 на valid і матриця помилок
        static string? Section2(StringBuilder md, TeamConfig config)
        {
            md.AppendLine("## 2. Класифікатор типу файла (перцептрон)");
            md.AppendLine();

            if (!Directory.Exists("data/train") || !Directory.Exists("data/valid"))
            {
                md.AppendLine("Немає `data/train` або `data/valid`: класифікатор не навчено.");
                md.AppendLine();
                return null;
            }

            List<Sample> train = Dataset.Load("data/train");
            List<Sample> valid = Dataset.Load("data/valid");
            // Seed для кожної ініціалізації: 1, 2, ..., скільки задав викладач у team_config.json
            int[] seeds = Enumerable.Range(1, Math.Max(1, config.Initializations)).ToArray();

            md.AppendLine($"Протокол: навчання на train ({train.Count} файлів), вибір моделі за macro-F1 на valid ({valid.Count} файлів) з {seeds.Length} ініціалізацій.");
            md.AppendLine();
            md.AppendLine("| seed | macro-F1 на valid |");
            md.AppendLine("|---:|---:|");

            FileClassifier? best = null;
            double bestF1 = -1;
            foreach (int seed in seeds)
            {
                FileClassifier candidate = FileClassifier.TrainBest(train, valid, new[] { seed }, 300, 0.1, false);
                double f1 = Metrics.MacroF1(Metrics.Confusion(candidate.Net, valid));
                md.AppendLine($"| {seed} | {f1.ToString("F4", c)} |");
                if (f1 > bestF1)
                {
                    bestF1 = f1;
                    best = candidate;
                }
            }
            md.AppendLine();

            best!.Save(ModelPath);
            int[,] confusion = Metrics.Confusion(best.Net, valid);

            md.AppendLine($"Найкраща модель: **macro-F1 = {bestF1.ToString("F4", c)}**, поріг F1_min = {config.F1Min.ToString(c)}: **{(bestF1 >= config.F1Min ? "пройдено" : "НЕ пройдено")}**. "
                + $"Базові лінії: один клас {config.BaselineMajority.ToString(c)}, випадковий вибір {config.BaselineRandom.ToString(c)}. "
                + $"Ваги збережено у `{ModelPath}`.");
            md.AppendLine();
            md.AppendLine("Матриця помилок на valid (рядки - справжній тип, стовпці - передбачений):");
            md.AppendLine();

            int n = FileClassifier.ClassCount;
            var header = new StringBuilder("| справжній \\ передбачений |");
            var separator = new StringBuilder("|---|");
            for (int j = 0; j < n; j++)
            {
                header.Append($" {j} |");
                separator.Append("---:|");
            }
            md.AppendLine(header.ToString());
            md.AppendLine(separator.ToString());
            for (int i = 0; i < n; i++)
            {
                var row = new StringBuilder($"| {i} {FileClassifier.classNames[i]} |");
                for (int j = 0; j < n; j++)
                    row.Append($" {confusion[i, j]} |");
                md.AppendLine(row.ToString());
            }
            md.AppendLine();
            md.AppendLine("Підсумковий macro-F1 на прихованій вибірці holdout/ рахує викладач.");
            md.AppendLine();
            return ModelPath;
        }

        // 3. Прогін усього корпусу в кількох режимах: Q, побайтове відновлення, лог без необроблених винятків
        static void Section3(StringBuilder md, List<ArchiveFile> corpus, TeamConfig config, string? model)
        {
            if (File.Exists(CorpusLogPath))
                File.Delete(CorpusLogPath);

            var modes = new List<(string Name, Settings Settings)>
            {
                ("жадібний розбір", new Settings { Parser = ParserKind.Greedy }),
                ("лінивий розбір (за замовчуванням)", new Settings { Parser = ParserKind.Lazy }),
                ("ДП-розбір", new Settings { Parser = ParserKind.Dp }),
                ("ДП-розбір + ДП-блоки + прогноз", new Settings { Parser = ParserKind.Dp, UseDpBlockSplit = true, UsePrediction = true }),
            };
            if (model != null)
                modes.Add(("лінивий + класифікатор + стратегія за типом", new Settings { ClassifierPath = model, UseTypeStrategy = true }));
            modes.Add(("лінивий + вирівнювання томів до 4096", new Settings { PadUnit = 4096 }));

            md.AppendLine("## 3. Прогін усього корпусу: метрика Q і побайтове відновлення");
            md.AppendLine();
            md.AppendLine($"Q = {config.Alpha.ToString(c)}·S + {config.Beta.ToString(c)}·T, S = B_orig/(B_orig+B_arc), T = t_ref/(t_ref+t), t_ref = {config.TRef.ToString(c)} с; t - пакування + розпакування.");
            md.AppendLine();
            md.AppendLine("| режим | B_arc (Б) | коефіцієнт | t (с) | S | T | Q | відновлено побайтово |");
            md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");

            int failures = 0;
            QEvaluation? best = null;
            foreach (var (name, settings) in modes)
            {
                QEvaluation eval;
                try
                {
                    eval = QualityMetric.Evaluate(corpus, settings, config, name, CorpusLogPath);
                }
                catch (Exception e)
                {
                    // Сюди не має потрапляти нічого: Evaluate сам ловить помилки архіватора
                    File.AppendAllText(CorpusLogPath, $"[{DateTime.UtcNow:O}] [UNHANDLED] Mode='{name}' {e}\n");
                    failures++;
                    continue;
                }

                if (!eval.RestoredExact)
                    failures++;
                if (best == null || eval.Q > best.Q)
                    best = eval;

                double ratio = eval.ArchiveBytes > 0 ? (double)eval.OriginalBytes / eval.ArchiveBytes : 0;
                md.AppendLine(
                    $"| {name} | {eval.ArchiveBytes} | {ratio.ToString("F3", c)} | {eval.ElapsedSeconds.ToString("F3", c)} | "
                        + $"{eval.S.ToString("F4", c)} | {eval.T.ToString("F4", c)} | {eval.Q.ToString("F4", c)} | "
                        + $"{(eval.RestoredExact ? "так, CRC32 збігається" : "НІ: " + eval.ErrorMessage)} |"
                );
            }
            md.AppendLine();

            double referenceRatio = (double)config.CorpusBytes / config.ReferenceArchiveBytes;
            md.AppendLine($"Опорний пакувальник викладача: коефіцієнт {referenceRatio.ToString("F3", c)} ({config.ReferenceArchiveBytes} байтів).");
            if (best != null)
                md.AppendLine($"Найвища Q у таблиці: {best.Q.ToString("F4", c)} ({best.Name}).");
            md.AppendLine($"Лог прогонів: `{CorpusLogPath}`. Режимів з помилкою або невідновленими файлами: **{failures}**; необроблених винятків: **{CountUnhandled()}**.");
            md.AppendLine();
        }

        static int CountUnhandled()
        {
            if (!File.Exists(CorpusLogPath))
                return 0;
            return File.ReadAllLines(CorpusLogPath).Count(line => line.Contains("[UNHANDLED]"));
        }

        // 5. ГА проти випадкового пошуку: таблиця порівняння і збіжність за поколіннями
        static void Section5(
            StringBuilder md,
            List<ArchiveFile> corpus,
            TeamConfig config,
            string? model,
            bool quick,
            int population,
            int generations
        )
        {
            md.AppendLine("## Таблиця 5. Збіжність ГА проти випадкового пошуку");
            md.AppendLine();

            if (quick)
            {
                md.AppendLine("Режим `--quick`: ГА не запускався, нижче - збережений журнал поколінь.");
                md.AppendLine();
                AppendTsvFile(md, GaLogPath);
                AppendTsvFile(md, GaAdvancedLogPath);
                return;
            }

            var basic = new GaRunOptions
            {
                Seed = config.Seed,
                PopulationSize = population,
                Generations = generations,
                LogPath = GaLogPath,
            };
            GaComparisonReport report = GeneticOptimizer.CompareWithRandomSearch(corpus, config, basic);
            md.AppendLine($"### Базовий ГА (3 гени, турнірна селекція, одноточковий кросовер, мутація, елітизм): популяція {population}, поколінь {generations}");
            md.AppendLine();
            AppendTables(md, report.TableText.Split('\n').Select(l => l.TrimEnd('\r')).ToList());
            AppendConvergence(md, report);

            var advanced = new GaRunOptions
            {
                Seed = config.Seed,
                Advanced = true,
                AdaptiveMutation = true,
                Crossover = CrossoverType.Uniform,
                LogPath = GaAdvancedLogPath,
                ClassifierPath = model,
            };
            GaComparisonReport advancedReport = GeneticOptimizer.CompareWithRandomSearch(corpus, config, advanced);
            md.AppendLine($"### Просунутий ГА (повна хромосома, рівномірний кросовер, адаптивна мутація) проти випадкового і локального пошуку");
            md.AppendLine();
            AppendTables(md, advancedReport.TableText.Split('\n').Select(l => l.TrimEnd('\r')).ToList());
            AppendConvergence(md, advancedReport);

            md.AppendLine($"Журнали поколінь: `{GaLogPath}`, `{GaAdvancedLogPath}`. Через елітизм найкраща Q не спадає від покоління до покоління.");
            md.AppendLine();
        }

        // Збіжність: найкраща Q ГА за поколіннями і найкраща Q випадкового пошуку після тієї самої кількості обчислень
        static void AppendConvergence(StringBuilder md, GaComparisonReport report)
        {
            md.AppendLine("| покоління | обчислень Q | найкраща Q ГА | середня Q ГА | найкраща Q випадкового пошуку |");
            md.AppendLine("|---:|---:|---:|---:|---:|");

            List<GaIndividual> randomOrder = report.RandomSearch.Evaluated;
            foreach (GaGenerationRecord row in report.Genetic.History)
            {
                double randomBest = 0;
                for (int i = 0; i < Math.Min(row.Evaluations, randomOrder.Count); i++)
                    randomBest = Math.Max(randomBest, randomOrder[i].Fitness);

                md.AppendLine(
                    $"| {row.Generation} | {row.Evaluations} | {row.BestQ.ToString("F4", c)} | {row.AverageQ.ToString("F4", c)} | "
                        + $"{(randomOrder.Count > 0 ? randomBest.ToString("F4", c) : "-")} |"
                );
            }
            md.AppendLine();
        }

        static void AppendTsvFile(StringBuilder md, string path)
        {
            if (!File.Exists(path))
                return;
            md.AppendLine($"`{path}`:");
            md.AppendLine();
            AppendTables(md, File.ReadAllLines(path).ToList());
        }

        // Перетворює рядки з табуляцією на таблиці Markdown. Рядок без табуляції - звичайний текст
        public static void AppendTables(StringBuilder md, List<string> lines)
        {
            bool inTable = false;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                if (line.Contains('\t'))
                {
                    string[] cells = line.Split('\t').Select(Escape).ToArray();
                    if (!inTable)
                    {
                        md.AppendLine("| " + string.Join(" | ", cells) + " |");
                        md.AppendLine("|" + string.Concat(cells.Select(_ => "---|")));
                        inTable = true;
                    }
                    else
                    {
                        md.AppendLine("| " + string.Join(" | ", cells) + " |");
                    }
                    continue;
                }

                if (inTable)
                {
                    md.AppendLine();
                    inTable = false;
                }
                if (line.Trim().Length > 0)
                {
                    md.AppendLine(line);
                    md.AppendLine();
                }
            }
            if (inTable)
                md.AppendLine();
        }

        static string Escape(string cell) => cell.Replace("|", "\\|");
    }
}
