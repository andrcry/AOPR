namespace SimpleArchiver
{
    // Багатошаровий перцептрон як послідовність шарів
    // Навчання - зворотне поширення помилки. Функція втрат E = 0.5 * sum (a - t)^2
    public class Mlp
    {
        public readonly Layer[] layers; // шари мережі по черзі
        public readonly int[] sizes; // кількість нейронів у кожному шарі, разом із входом

        // sizes = {52, 24, 7}: вхід, прихований шар, вихід (7 типів файлів)
        public Mlp(int[] sizes, int seed)
        {
            this.sizes = sizes;
            var rnd = new Random(seed);
            layers = new Layer[sizes.Length - 1];
            for (int l = 0; l < layers.Length; l++)
                layers[l] = new Layer(sizes[l], sizes[l + 1], rnd);
        }

        // Прямий прохід через усю мережу
        public double[] Forward(double[] x)
        {
            double[] a = x;

            foreach (var layer in layers)
                a = layer.Forward(a);

            return a;
        }

        // Номер нейрона з найбільшим виходом - це і є передбачений клас
        public int PredictClass(double[] x)
        {
            double[] output = Forward(x);
            int best = 0;
            for (int j = 1; j < output.Length; j++)
            {
                if (output[j] > output[best])
                    best = j;
            }
            return best;
        }

        // Функція втрат E = 0.5 * sum (a - t)^2
        public static double Loss(double[] output, double[] target)
        {
            double e = 0;
            for (int j = 0; j < output.Length; j++)
                e += 0.5 * (output[j] - target[j]) * (output[j] - target[j]);
            return e;
        }

        // Зворотний прохід: обчислення delta у всіх шарах. Викликати ПІСЛЯ Forward для того самого прикладу
        public void Backward(double[] target)
        {
            // Вихідний шар: delta_j = (a_j - t_j) * a_j * (1 - a_j)
            Layer last = layers[^1];
            for (int j = 0; j < last.outSize; j++)
            {
                double a = last.output[j];
                last.delta[j] = (a - target[j]) * a * (1 - a);
            }

            // Приховані шари - від передостаннього до першого
            for (int l = layers.Length - 2; l >= 0; l--)
            {
                Layer cur = layers[l],
                    next = layers[l + 1];
                for (int i = 0; i < cur.outSize; i++)
                {
                    double sum = 0;
                    for (int j = 0; j < next.outSize; j++)
                        sum += next.weights[j, i] * next.delta[j];

                    double a = cur.output[i];
                    cur.delta[i] = sum * a * (1 - a);
                }
            }
        }

        // Оновлення ваг градієнтним спуском: dE/dW[j,i] = delta_j * x_i,  dE/db_j = delta_j
        public void Update(double eta)
        {
            foreach (var layer in layers)
                for (int j = 0; j < layer.outSize; j++)
                {
                    for (int i = 0; i < layer.inSize; i++)
                        layer.weights[j, i] -= eta * layer.delta[j] * layer.input[i];
                    layer.biases[j] -= eta * layer.delta[j];
                }
        }

        // Чисельна перевірка градієнта: порівнює delta_j * x_i з (E(w+eps) - E(w-eps)) / (2 * eps)
        public double GradientCheck(double[] x, double[] t, double eps = 1e-5)
        {
            Forward(x);
            Backward(t);

            var analytic = new double[layers.Length][,];
            for (int l = 0; l < layers.Length; l++)
            {
                var layer = layers[l];
                analytic[l] = new double[layer.outSize, layer.inSize];
                for (int j = 0; j < layer.outSize; j++)
                for (int i = 0; i < layer.inSize; i++)
                    analytic[l][j, i] = layer.delta[j] * layer.input[i];
            }

            double maxDiff = 0;
            for (int l = 0; l < layers.Length; l++)
            {
                var layer = layers[l];
                for (int j = 0; j < layer.outSize; j++)
                for (int i = 0; i < layer.inSize; i++)
                {
                    double w = layer.weights[j, i];
                    layer.weights[j, i] = w + eps;
                    double ePlus = Loss(Forward(x), t);
                    layer.weights[j, i] = w - eps;
                    double eMinus = Loss(Forward(x), t);
                    layer.weights[j, i] = w;
                    double numeric = (ePlus - eMinus) / (2 * eps);
                    maxDiff = Math.Max(maxDiff, Math.Abs(numeric - analytic[l][j, i]));
                }
            }
            Forward(x);
            return maxDiff;
        }
    }
}
