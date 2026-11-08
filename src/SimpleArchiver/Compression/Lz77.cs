namespace SimpleArchiver
{
    // Розбір LZ77: замінює повтори посиланнями назад
    public static class Lz77
    {
        public const int MinMatch = 3; // найкоротший збіг, який варто записувати посиланням
        public const int MaxMatch = 258; // найдовший збіг
        public const int WindowSize = 32768; // як далеко назад можна шукати збіг

        // Фіксовані вартості з розділу 6 специфікації (у бітах)
        public const int LiteralCost = 9;

        // Вартість посилання у бітах за специфікацією
        public static int MatchCost(int distance) =>
            17 + Math.Max(0, BlockCoder.DistanceCode(distance) - 1);

        // Розбирає дані обраним способом
        public static List<Token> Parse(byte[] data, ParserKind kind, int maxChain)
        {
            if (kind == ParserKind.Greedy)
                return ParseGreedy(data, maxChain);
            if (kind == ParserKind.Lazy)
                return ParseLazy(data, maxChain);
            return ParseDp(data, maxChain);
        }

        // Кожен байт - літерал. Потрібно для порівняння
        public static List<Token> ParseLiteralsOnly(byte[] data)
        {
            var tokens = new List<Token>();

            foreach (byte b in data)
                tokens.Add(Token.MakeLiteral(b));

            return tokens;
        }

        // Жадібний розбір. У кожній позиції бере найдовший знайдений збіг
        public static List<Token> ParseGreedy(byte[] data, int maxChain)
        {
            var tokens = new List<Token>();
            var finder = new MatchFinder(data, maxChain);

            int i = 0;
            while (i < data.Length)
            {
                var match = finder.FindLongest(i);

                if (match.Length >= MinMatch)
                {
                    tokens.Add(Token.MakeMatch(match.Length, match.Distance));
                    i += match.Length;
                }
                else
                {
                    tokens.Add(Token.MakeLiteral(data[i]));
                    i++;
                }
            }

            return tokens;
        }

        // Лінивий розбір. Якщо в наступній позиції збіг довший, тут пише літерал
        public static List<Token> ParseLazy(byte[] data, int maxChain)
        {
            var tokens = new List<Token>();
            var finder = new MatchFinder(data, maxChain);

            int i = 0;
            while (i < data.Length)
            {
                var match = finder.FindLongest(i);

                if (match.Length < MinMatch)
                {
                    tokens.Add(Token.MakeLiteral(data[i]));
                    i++;
                    continue;
                }

                if (i + 1 < data.Length && finder.FindLongest(i + 1).Length > match.Length)
                {
                    tokens.Add(Token.MakeLiteral(data[i]));
                    i++;
                    continue;
                }

                tokens.Add(Token.MakeMatch(match.Length, match.Distance));
                i += match.Length;
            }

            return tokens;
        }

        // ДП-розбір за фіксованими вартостями (просунутий рівень).
        // C[i] - найменша вартість у бітах закодувати хвіст даних з позиції i.
        //   C[n] = 0
        //   C[i] = min( 9 + C[i+1],  min по (d, L) з M_i [ 17 + extra(d) + C[i+L] ] )
        // Йдемо з кінця, бо C[i] залежить від C з більшими номерами
        public static List<Token> ParseDp(byte[] data, int maxChain)
        {
            int n = data.Length;

            // Знайти множину збігів M_i для кожної позиції
            var candidates = new List<(int Length, int Distance)>[n];
            var finder = new MatchFinder(data, maxChain);
            for (int i = 0; i < n; i++)
                candidates[i] = finder.FindAll(i);

            // Заповнити таблицю C з кінця
            var cost = new long[n + 1]; // cost[n] = 0
            var stepLength = new int[n]; // 1 - літерал, інакше довжина посилання
            var stepDistance = new int[n];

            for (int i = n - 1; i >= 0; i--)
            {
                // Варіант 1: літерал
                cost[i] = LiteralCost + cost[i + 1];
                stepLength[i] = 1;

                // Варіант 2: посилання. Довжину L можна брати будь-яку від 3 до довжини збігу.
                // Коротші L уже перевірені з ближчим (дешевшим) збігом, тому починаємо з shorter + 1
                int shorter = MinMatch - 1;

                foreach (var match in candidates[i])
                {
                    int matchCost = MatchCost(match.Distance);

                    for (int length = shorter + 1; length <= match.Length; length++)
                    {
                        if (matchCost + cost[i + length] < cost[i])
                        {
                            cost[i] = matchCost + cost[i + length];
                            stepLength[i] = length;
                            stepDistance[i] = match.Distance;
                        }
                    }

                    shorter = match.Length;
                }
            }

            // Відновити розбір від початку за запам'ятованими кроками
            var tokens = new List<Token>();
            int pos = 0;

            while (pos < n)
            {
                if (stepLength[pos] == 1)
                    tokens.Add(Token.MakeLiteral(data[pos]));
                else
                    tokens.Add(Token.MakeMatch(stepLength[pos], stepDistance[pos]));

                pos += stepLength[pos];
            }

            return tokens;
        }

        // Вартість розбору за фіксованими вартостями (щоб порівнювати розбори між собою)
        public static long FixedCost(List<Token> tokens)
        {
            long sum = 0;
            foreach (Token token in tokens)
                sum += token.IsMatch ? MatchCost(token.Distance) : LiteralCost;
            return sum;
        }
    }
}
