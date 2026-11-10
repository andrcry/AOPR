namespace SimpleArchiver
{
    // Вектор ознак файла для перцептрона (розділ 3.1 завдання).
    // Усі ознаки приведені до проміжку [0; 1] (автокореляція - до [-1; 1])
    public static class FileFeatures
    {
        public const int SampleSize = 65536; // ознаки рахуємо на перших 64 КБ файла
        public const int HistogramBins = 32; // кількість кошиків гістограми
        public const int MaxLag = 16; // найбільший лаг автокореляції
        public const int Count = 1 + HistogramBins + 1 + 1 + MaxLag + 1; // 52 ознаки

        // Рахує всі 52 ознаки файла
        public static double[] Compute(byte[] fileData)
        {
            var features = new double[Count];

            int size = Math.Min(fileData.Length, SampleSize);
            if (size == 0)
                return features; // порожній файл - усі ознаки нульові

            var data = new byte[size];
            Array.Copy(fileData, data, size);

            int k = 0;

            // Ентропія Шеннона по байтах (максимум 8 бітів)
            features[k++] = ByteEntropy(data) / 8.0;

            // Гістограма байтів, згорнута до 32 кошиків
            foreach (double share in Histogram(data))
                features[k++] = share;

            // Частка друкованих ASCII-символів
            features[k++] = PrintableShare(data);

            // Середня відстань між символами кінця рядка
            features[k++] = NewlineDistance(data);

            // Автокореляція на лагах 1-16 (виявляє растр і таблиці)
            foreach (double value in Autocorrelations(data, MaxLag))
                features[k++] = value;

            // Ентропія біграм (максимум 16 бітів)
            features[k++] = BigramEntropy(data) / 16.0;

            return features;
        }

        // H = -sum p * log2(p) по всіх значеннях байта
        public static double ByteEntropy(byte[] data)
        {
            var counts = new long[256];
            foreach (byte b in data)
                counts[b]++;

            return Entropy(counts, data.Length);
        }

        // Частка байтів у кожному з 32 кошиків (у кошику 8 сусідніх значень байта)
        static double[] Histogram(byte[] data)
        {
            var shares = new double[HistogramBins];
            foreach (byte b in data)
                shares[b / (256 / HistogramBins)]++;

            for (int i = 0; i < HistogramBins; i++)
                shares[i] /= data.Length;

            return shares;
        }

        // Друковані символи: від пробілу до '~', а також табуляція і переходи рядка
        public static double PrintableShare(byte[] data)
        {
            int printable = 0;
            foreach (byte b in data)
            {
                if ((b >= 32 && b <= 126) || b == '\t' || b == '\n' || b == '\r')
                    printable++;
            }
            return (double)printable / data.Length;
        }

        // Середня довжина рядка, стиснута в [0; 1]: d / (d + 80). Без рядків - 1
        static double NewlineDistance(byte[] data)
        {
            int count = 0;
            int first = -1;
            int last = -1;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != '\n')
                    continue;

                if (first < 0)
                    first = i;
                last = i;
                count++;
            }

            if (count < 2)
                return 1.0;

            double average = (double)(last - first) / (count - 1);
            return average / (average + 80.0);
        }

        // Коефіцієнти кореляції між байтом і байтом на lag позицій далі, для лагів 1..maxLag.
        // Середнє (mean) і сума квадратів відхилень (bottom) не залежать від лагу,
        // тому рахуються один раз, а не окремо для кожного з 16 лагів
        static double[] Autocorrelations(byte[] data, int maxLag)
        {
            var result = new double[maxLag];

            double mean = 0;
            foreach (byte b in data)
                mean += b;
            mean /= data.Length;

            double bottom = 0;
            for (int i = 0; i < data.Length; i++)
                bottom += (data[i] - mean) * (data[i] - mean);

            if (bottom == 0)
                return result; // усі байти однакові - кореляція 0 на всіх лагах

            for (int lag = 1; lag <= maxLag; lag++)
            {
                int n = data.Length - lag;
                if (n < 2)
                    continue; // замало пар для цього лагу - лишається 0

                double top = 0;
                for (int i = 0; i < n; i++)
                    top += (data[i] - mean) * (data[i + lag] - mean);

                result[lag - 1] = top / bottom;
            }

            return result;
        }

        // Ентропія пар сусідніх байтів
        static double BigramEntropy(byte[] data)
        {
            if (data.Length < 2)
                return 0;

            var counts = new long[65536];
            for (int i = 0; i + 1 < data.Length; i++)
                counts[(data[i] << 8) | data[i + 1]]++;

            return Entropy(counts, data.Length - 1);
        }

        // Ентропія за кількістю появ кожного значення
        static double Entropy(long[] counts, long total)
        {
            double h = 0;
            foreach (long count in counts)
            {
                if (count == 0)
                    continue;

                double p = (double)count / total;
                h -= p * Math.Log2(p);
            }
            return h;
        }
    }
}
