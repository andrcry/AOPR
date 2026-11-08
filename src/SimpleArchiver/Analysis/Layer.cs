namespace SimpleArchiver
{
    // Один повнозв'язний шар перцептрона
    public class Layer
    {
        public readonly int inSize; // кількість входів шару
        public readonly int outSize; // кількість нейронів шару
        public readonly double[,] weights; // ваги: weights[j, i] - від входу i до нейрона j
        public readonly double[] biases; // зсуви (bias) нейронів
        public double[] input = null!; // вхід шару (вихід попереднього шару)
        public readonly double[] output; // вихід шару a = sigmoid(z)
        public readonly double[] delta; // локальні градієнти delta = dE/dz

        // Створює шар і заповнює ваги випадковими числами за правилом Xavier
        public Layer(int inSize, int outSize, Random rnd)
        {
            this.inSize = inSize;
            this.outSize = outSize;
            weights = new double[outSize, inSize];
            biases = new double[outSize];
            output = new double[outSize];
            delta = new double[outSize];

            // Ініціалізація Xavier: W ~ U(-L, L),  L = sqrt(6 / (inSize + outSize))
            double limit = Math.Sqrt(6.0 / (inSize + outSize));
            for (int j = 0; j < outSize; j++)
            for (int i = 0; i < inSize; i++)
                weights[j, i] = (2 * rnd.NextDouble() - 1) * limit;
        }

        // Сигмоїда: стискає будь-яке число в проміжок від 0 до 1
        public static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));

        // Прямий прохід через шар: z = W * x + b, a = sigmoid(z)
        public double[] Forward(double[] input)
        {
            this.input = input;

            for (int j = 0; j < outSize; j++)
            {
                double z = biases[j];

                for (int i = 0; i < inSize; i++)
                    z += weights[j, i] * input[i];

                output[j] = Sigmoid(z);
            }
            return output;
        }
    }
}
