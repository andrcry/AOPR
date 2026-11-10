namespace SimpleArchiver
{
    // Канонічні коди (розділ 3.4). В архів пишуться тільки довжини, коди рахуються з них
    public static class CanonicalCodes
    {
        public const int MaxLength = 15; // найбільша довжина коду

        // Повертає код для кожного символу за його довжиною
        public static int[] Make(int[] lengths)
        {
            // Скільки символів має кожну довжину
            var countOfLength = new int[MaxLength + 1];
            foreach (int len in lengths)
            {
                if (len > 0)
                    countOfLength[len]++;
            }

            // Перший код для кожної довжини
            var nextCode = new int[MaxLength + 1];
            int code = 0;
            for (int len = 1; len <= MaxLength; len++)
            {
                code = (code + countOfLength[len - 1]) << 1;
                nextCode[len] = code;
            }

            // Символи за зростанням отримують коди підряд
            var codes = new int[lengths.Length];
            for (int s = 0; s < lengths.Length; s++)
            {
                if (lengths[s] > 0)
                    codes[s] = nextCode[lengths[s]]++;
            }

            return codes;
        }

        // Перевіряє рівність Крафта: сума 2^(-L) дорівнює 1. Множимо все на 2^15, щоб не було дробів
        public static bool IsComplete(int[] lengths)
        {
            long sum = 0;
            foreach (int len in lengths)
            {
                if (len > 0)
                    sum += 1L << (MaxLength - len);
            }
            return sum == 1L << MaxLength;
        }
    }
}
