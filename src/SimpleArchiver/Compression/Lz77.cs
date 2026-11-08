namespace SimpleArchiver
{
    // Розбір LZ77: замінює повтори посиланнями назад
    public static class Lz77
    {
        public const int MinMatch = 3; // найкоротший збіг, який варто записувати посиланням
        public const int MaxMatch = 258; // найдовший збіг
        public const int WindowSize = 32768; // як далеко назад можна шукати збіг

        // Розбирає дані обраним способом
        public static List<Token> Parse(byte[] data, ParserKind kind, int maxChain)
        {
            if (kind == ParserKind.Greedy)
                return ParseGreedy(data, maxChain);
            return ParseLazy(data, maxChain);
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
    }
}
