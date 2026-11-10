using System.Globalization;

namespace SimpleArchiver
{
    // Класифікатор типу файла (базовий рівень зони Аналіз файлу).
    // Перцептрон: 52 входи (ознаки з FileFeatures), 24 нейрони прихованого шару і 7 виходів (типи файлів)
    public class FileClassifier
    {
        public const int ClassCount = 7; // кількість типів файлів
        public const int HiddenSize = 24; // кількість нейронів прихованого шару
        public const int AlreadyCompressed = 5; // тип вже стиснуте: такі файли не стискаємо

        // назви типів файлів 0-6 для виведення на екран
        public static readonly string[] classNames =
        {
            "природний текст",
            "вихідний код",
            "структуровані дані",
            "растр без стиснення",
            "виконуваний файл",
            "вже стиснуте",
            "штучні дані",
        };

        public Mlp Net { get; } // навчена мережа

        // Створює класифікатор з готової мережі
        public FileClassifier(Mlp net)
        {
            Net = net;
        }

        // Повертає номер типу файла 0-6
        public int Classify(byte[] fileData) => Net.PredictClass(FileFeatures.Compute(fileData));

        // Навчання за протоколом: train/ - навчання, valid/ - вибір найкращої з кількох ініціалізацій
        public static FileClassifier TrainBest(
            List<Sample> train,
            List<Sample> valid,
            int[] seeds,
            int epochs,
            double eta,
            bool verbose
        )
        {
            Mlp? bestNet = null;
            double bestF1 = -1;

            foreach (int seed in seeds)
            {
                var net = new Mlp(new[] { FileFeatures.Count, HiddenSize, ClassCount }, seed);
                Train(net, train, epochs, eta, seed);

                double f1 = Metrics.MacroF1(Metrics.Confusion(net, valid));
                if (verbose)
                    Console.WriteLine($"Ініціалізація seed = {seed}: macro-F1 на valid = {f1:F4}");

                if (f1 > bestF1)
                {
                    bestF1 = f1;
                    bestNet = net;
                }
            }

            return new FileClassifier(bestNet!);
        }

        // Стохастичний градієнтний спуск, як у практичному занятті з XOR
        public static void Train(Mlp net, List<Sample> samples, int epochs, double eta, int seed)
        {
            var rnd = new Random(seed);
            var order = new int[samples.Count];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;

            for (int epoch = 1; epoch <= epochs; epoch++)
            {
                // Перемішуємо порядок прикладів (алгоритм Фішера-Єйтса)
                for (int k = order.Length - 1; k > 0; k--)
                {
                    int r = rnd.Next(k + 1);
                    (order[k], order[r]) = (order[r], order[k]);
                }

                foreach (int n in order)
                {
                    net.Forward(samples[n].Features);
                    net.Backward(OneHot(samples[n].Label));
                    net.Update(eta);
                }
            }
        }

        // Перетворює номер типу на масив із семи чисел, де одиниця стоїть лише на місці цього типу
        static double[] OneHot(int label)
        {
            var target = new double[ClassCount];
            target[label] = 1;
            return target;
        }

        // Зберігає ваги у текстовий файл: перший рядок - розміри шарів, далі рядки ваг і зсув нейрона
        public void Save(string path)
        {
            var lines = new List<string>();
            lines.Add(string.Join(" ", Net.sizes));

            foreach (Layer layer in Net.layers)
            {
                for (int j = 0; j < layer.outSize; j++)
                {
                    string line = "";
                    for (int i = 0; i < layer.inSize; i++)
                        line +=
                            layer.weights[j, i].ToString("R", CultureInfo.InvariantCulture) + " ";
                    line += layer.biases[j].ToString("R", CultureInfo.InvariantCulture);
                    lines.Add(line);
                }
            }

            File.WriteAllLines(path, lines);
        }

        // Читає ваги мережі з текстового файла
        public static FileClassifier Load(string path)
        {
            string[] lines = File.ReadAllLines(path);

            string[] sizeParts = lines[0].Split(' ');
            var sizes = new int[sizeParts.Length];
            for (int i = 0; i < sizes.Length; i++)
                sizes[i] = int.Parse(sizeParts[i]);

            if (sizes[0] != FileFeatures.Count || sizes[sizes.Length - 1] != ClassCount)
                throw new InvalidOperationException(
                    "файл класифікатора не підходить до цієї версії програми"
                );

            var net = new Mlp(sizes, 0);
            int row = 1;

            foreach (Layer layer in net.layers)
            {
                for (int j = 0; j < layer.outSize; j++)
                {
                    string[] numbers = lines[row].Split(' ');
                    for (int i = 0; i < layer.inSize; i++)
                        layer.weights[j, i] = double.Parse(
                            numbers[i],
                            CultureInfo.InvariantCulture
                        );
                    layer.biases[j] = double.Parse(
                        numbers[layer.inSize],
                        CultureInfo.InvariantCulture
                    );
                    row++;
                }
            }

            return new FileClassifier(net);
        }
    }
}
