namespace SimpleArchiver
{
    // Прогноз наступного байта (просунутий рівень зони Аналіз файлу).
    //
    // Мережа з одного лінійного нейрона: за k попередніми байтами передбачає наступний
    //   прогноз = b + w1 * x[t-1] + w2 * x[t-2] + ... + wk * x[t-k]
    // Замість байта записуємо залишок r = (x - прогноз) mod 256. Для растру й плавних даних
    // залишки малі й часто однакові, тому стискаються краще за самі байти.
    //
    // Ваги записуються в архів як цілі числа (вага * 4096). Прогноз рахується тільки
    // цілими числами, тому при розпакуванні виходить рівно той самий прогноз
    public class BytePredictor
    {
        public const int MaxHistory = 16; // найбільша кількість попередніх байтів, яку бачить прогноз
        const int shift = 12; // ваги зберігаються помноженими на 2^12 = 4096
        const int scale = 1 << shift; // множник для перетворення ваг у цілі числа

        public int[] Weights { get; } // k цілих ваг
        public int Bias { get; } // зсув нейрона, теж ціле число

        public int History => Weights.Length; // скільки попередніх байтів бачить прогноз

        // Створює прогноз із готовими цілими вагами і зсувом
        public BytePredictor(int[] weights, int bias)
        {
            Weights = weights;
            Bias = bias;
        }

        // Навчання нейрона правилом Відроу-Хоффа (градієнтний спуск для E = 0.5 * (прогноз - x)^2)
        public static BytePredictor Train(
            byte[] data,
            int history,
            int epochs = 2,
            double eta = 0.01
        )
        {
            if (history < 1 || history > MaxHistory)
                throw new ArgumentException($"довжина історії має бути від 1 до {MaxHistory}");

            // Початкові ваги: прогноз = попередній байт
            var w = new double[history];
            w[0] = 1;
            double b = 0;

            // Кілька проходів по даних. Байти ділимо на 255, щоб числа були в [0; 1]
            var x = new double[history];
            for (int epoch = 0; epoch < epochs; epoch++)
            {
                for (int t = 0; t < data.Length; t++)
                {
                    for (int i = 0; i < history; i++)
                        x[i] = t - 1 - i >= 0 ? data[t - 1 - i] / 255.0 : 0;

                    double prediction = b;
                    for (int i = 0; i < history; i++)
                        prediction += w[i] * x[i];

                    double error = prediction - data[t] / 255.0;

                    for (int i = 0; i < history; i++)
                        w[i] -= eta * error * x[i];
                    b -= eta * error;
                }
            }

            // Перетворити ваги на цілі числа
            var weights = new int[history];
            for (int i = 0; i < history; i++)
                weights[i] = (int)Math.Round(Math.Clamp(w[i], -100.0, 100.0) * scale);

            int bias = (int)Math.Round(Math.Clamp(b, -100.0, 100.0) * 255 * scale);
            return new BytePredictor(weights, bias);
        }

        // Прогноз для позиції t за байтами data[t-1], data[t-2], ... Лише цілі числа
        public int Predict(byte[] data, int t)
        {
            long sum = Bias;
            for (int i = 0; i < History; i++)
            {
                if (t - 1 - i >= 0)
                    sum += (long)Weights[i] * data[t - 1 - i];
            }

            long prediction = (sum + scale / 2) >> shift; // ділимо на 4096 з округленням
            return (int)Math.Clamp(prediction, 0, 255);
        }

        // Залишки: r[t] = (x[t] - прогноз) mod 256
        public byte[] MakeResiduals(byte[] data)
        {
            var residuals = new byte[data.Length];
            for (int t = 0; t < data.Length; t++)
                residuals[t] = (byte)(data[t] - Predict(data, t));
            return residuals;
        }

        // Відновлення: x[t] = (r[t] + прогноз) mod 256. Прогноз бере вже відновлені байти
        public byte[] Restore(byte[] residuals)
        {
            var data = new byte[residuals.Length];
            for (int t = 0; t < data.Length; t++)
                data[t] = (byte)(residuals[t] + Predict(data, t));
            return data;
        }

        // Запис в архів (командне розширення PREDICTION)
        // Стиснутий блок починається з k (1 байт). k = 0 - прогноз для блока не використано.
        // Якщо k > 0, далі bias і k ваг по 4 байти
        public byte[] ToBytes()
        {
            var writer = new ByteWriter();
            writer.WriteU8(History);
            writer.WriteU32((uint)Bias);
            foreach (int weight in Weights)
                writer.WriteU32((uint)weight);
            return writer.ToArray();
        }

        // Заголовок блока, у якому прогноз не використано: один байт k = 0
        public static byte[] NoPredictionHeader() => new byte[] { 0 };

        // Читає заголовок прогнозу. Повертає null, якщо k = 0
        public static BytePredictor? Read(ByteReader reader)
        {
            int history = reader.ReadU8();
            if (history == 0)
                return null;
            if (history > MaxHistory)
                throw new ArchiveException(
                    ErrorCodes.Code,
                    $"неправильна довжина історії прогнозу {history}"
                );

            int bias = (int)reader.ReadU32();
            var weights = new int[history];
            for (int i = 0; i < history; i++)
                weights[i] = (int)reader.ReadU32();

            return new BytePredictor(weights, bias);
        }
    }
}
