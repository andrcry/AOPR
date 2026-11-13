using System.Text;

namespace SimpleArchiver
{
    // Автоматичні контрольні точки
    // Запуск: dotnet run --project src/SimpleArchiver -- check
    static class Checks
    {
        // Запускає всі контрольні точки і повертає true, якщо всі пройдені
        public static bool RunAll()
        {
            bool ok = true;

            ok &= Crc();
            ok &= HuffmanLengths();
            ok &= SpecificationStream();
            ok &= DpParse();
            ok &= DpSplit();
            ok &= Knapsack();
            ok &= Perceptron();
            ok &= Predictor();
            ok &= GeneticAlgorithm();

            Console.WriteLine();
            Console.WriteLine(
                ok
                    ? "Усі контрольні точки пройдені."
                    : "Є помилки. Виправте крок, позначений як ПОМИЛКА."
            );
            return ok;
        }

        // Друкує результат однієї контрольної точки
        static void Print(string step, bool ok, string details)
        {
            Console.WriteLine($"{step}: {(ok ? "OK" : "ПОМИЛКА")}. {details}");
        }

        // 40 повторів "hello world " - файл з контрольного прикладу специфікації
        static byte[] Hello480()
        {
            string text = "";
            for (int i = 0; i < 40; i++)
                text += "hello world ";
            return Encoding.ASCII.GetBytes(text);
        }

        // Контроль 1: стандартне значення CRC32
        static bool Crc()
        {
            uint crc = Crc32.Compute(Encoding.ASCII.GetBytes("123456789"));
            bool ok = crc == 0xCBF43926;
            Print("CRC32", ok, $"отримано {crc:X8}, очікується CBF43926");
            return ok;
        }

        // Контроль 2: довжини кодів Гаффмана для прикладу специфікації
        static bool HuffmanLengths()
        {
            var counts = new long[257];
            foreach (char c in "hello world ")
                counts[c]++;
            counts[256] = 2;

            int[] lengths = HuffmanTree.BuildCodeLengths(counts);
            bool ok =
                lengths['l'] == 2
                && lengths[' '] == 3
                && lengths['w'] == 3
                && lengths['d'] == 4
                && lengths[256] == 3;
            Print(
                "Гаффман",
                ok,
                $"l = {lengths['l']}, пробіл = {lengths[' ']}, d = {lengths['d']}; очікується 2, 3, 4"
            );
            return ok;
        }

        // Контроль 3: бітовий потік контрольного прикладу ED 06 A3 F3 15 9D 18
        static bool SpecificationStream()
        {
            byte[] block = BlockCoder.Encode(Lz77.ParseGreedy(Hello480(), 64));
            var stream = new byte[7];
            Array.Copy(block, block.Length - 7, stream, 0, 7);

            string hex = Convert.ToHexString(stream);
            bool ok = block.Length == 57 && hex == "ED06A3F3159D18";
            Print(
                "Блок",
                ok,
                $"coded_len = {block.Length}, потік {hex}; очікується 57 і ED06A3F3159D18"
            );
            return ok;
        }

        // Контроль 4: ДП-розбір не дорожчий за жадібний і лінивий
        static bool DpParse()
        {
            byte[] data = Encoding.ASCII.GetBytes(
                "xabQQQQabcdefgh##xabcdefgh, abcabcabcabc abcdefgh"
            );

            long greedy = Lz77.FixedCost(Lz77.ParseGreedy(data, 64));
            long lazy = Lz77.FixedCost(Lz77.ParseLazy(data, 64));
            List<Token> dpTokens = Lz77.ParseDp(data, 64);
            long dp = Lz77.FixedCost(dpTokens);

            byte[] restored = BlockCoder.Decode(BlockCoder.Encode(dpTokens), data.Length);
            bool ok =
                dp <= greedy
                && dp <= lazy
                && Encoding.ASCII.GetString(restored) == Encoding.ASCII.GetString(data);
            Print(
                "ДП-розбір",
                ok,
                $"вартість: жадібний {greedy}, лінивий {lazy}, ДП {dp} бітів; ДП має бути найменшим"
            );
            return ok;
        }

        // Контроль 5: ДП-розбиття не гірше за фіксоване
        static bool DpSplit()
        {
            // Текст, потім шум, потім нулі - різні частини краще стискаються окремими блоками
            var data = new byte[12000];
            byte[] text = Hello480();
            for (int i = 0; i < 4000; i++)
                data[i] = text[i % text.Length];
            var random = new Random(1);
            for (int i = 4000; i < 8000; i++)
                data[i] = (byte)random.Next(256);

            var settings = new Settings();
            settings.BlockSize = 8192;
            settings.SplitUnit = 1024;

            long fixedSize = BlockSplitter.TotalSize(
                BlockSplitter.SplitFixed(data, settings, true)
            );
            long dpSize = BlockSplitter.TotalSize(BlockSplitter.SplitDp(data, settings));

            bool ok = dpSize <= fixedSize;
            Print("ДП-розбиття", ok, $"фіксоване {fixedSize} байтів, ДП {dpSize} байтів");
            return ok;
        }

        // Контроль 6: рюкзак ДП знаходить 10, жадібний лише 7
        static bool Knapsack()
        {
            int[] weights = { 6, 5, 5 };
            int[] values = { 7, 5, 5 };

            long dp = SimpleArchiver.Knapsack.TotalValue(
                SimpleArchiver.Knapsack.SolveDp(weights, values, 10),
                values
            );
            long greedy = SimpleArchiver.Knapsack.TotalValue(
                SimpleArchiver.Knapsack.SolveGreedy(weights, values, 10),
                values
            );

            bool ok = dp == 10 && greedy == 7;
            Print("Рюкзак", ok, $"ДП {dp}, жадібний {greedy}; очікується 10 і 7");
            return ok;
        }

        // Контроль 7: аналітичний градієнт перцептрона збігається з чисельним
        static bool Perceptron()
        {
            var net = new Mlp(new[] { FileFeatures.Count, 6, FileClassifier.ClassCount }, 1);
            double[] x = FileFeatures.Compute(Hello480());
            var target = new double[FileClassifier.ClassCount];
            target[0] = 1;

            double diff = net.GradientCheck(x, target);
            bool ok = diff < 1e-7;
            Print(
                "Перцептрон",
                ok,
                $"найбільша різниця чисельного й аналітичного градієнта {diff:E2}, має бути менше 1E-007"
            );
            return ok;
        }

        // Контроль 8: прогноз байта - залишки повертаються в ті самі байти
        static bool Predictor()
        {
            var data = new byte[5000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(128 + 100 * Math.Sin(i / 40.0));

            BytePredictor predictor = BytePredictor.Train(data, 4);
            byte[] restored = predictor.Restore(predictor.MakeResiduals(data));

            bool ok = Convert.ToHexString(restored) == Convert.ToHexString(data);
            Print("Прогноз байта", ok, "із залишків відновлено початкові дані");
            return ok;
        }

        // Контроль 9: ГА (кейс №17 елітизм) та чесне порівняння з випадковим пошуком
        static bool GeneticAlgorithm()
        {
            var corpus = new List<ArchiveFile>
            {
                new ArchiveFile("hello.txt", Hello480()),
                new ArchiveFile("smooth.bin", MakeSmooth(4096)),
            };
            TeamConfig config = TeamConfig.LoadIfExists(TeamConfig.DefaultPath);
            string tempLog = Path.Combine(
                Path.GetTempPath(),
                "sarc-ga-check-" + Guid.NewGuid().ToString("N") + ".tsv"
            );

            try
            {
                var options = new GaRunOptions
                {
                    PopulationSize = 4,
                    Generations = 2,
                    ElitismCount = 1,
                    Seed = config.Seed,
                    LogPath = tempLog,
                };

                GaComparisonReport report = GeneticOptimizer.CompareWithRandomSearch(
                    corpus,
                    config,
                    options
                );

                double bestGen0 = report.Genetic.History[0].BestQ;
                double bestLast = report.Genetic.History[^1].BestQ;
                bool ok =
                    report.Genetic.History.Count == 3
                    && report.Genetic.Best.Evaluation.RestoredExact
                    && report.RandomSearch.Best.Evaluation.RestoredExact
                    && bestLast >= bestGen0
                    && report.Genetic.Evaluations == report.RandomSearch.Evaluations
                    && File.Exists(tempLog);

                Print(
                    "Генетичний алгоритм",
                    ok,
                    $"Q(gen0)={bestGen0:F4}, Q(last)={bestLast:F4}, Q(random)={report.RandomSearch.Best.Fitness:F4}, обчислень={report.Genetic.Evaluations}"
                );
                return ok;
            }
            finally
            {
                if (File.Exists(tempLog))
                    File.Delete(tempLog);
            }
        }

        static byte[] MakeSmooth(int count)
        {
            var data = new byte[count];
            for (int i = 0; i < count; i++)
                data[i] = (byte)(128 + 100 * Math.Sin(i / 40.0));
            return data;
        }
    }
}
