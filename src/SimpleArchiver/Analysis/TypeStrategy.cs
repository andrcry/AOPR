namespace SimpleArchiver
{
    // Параметри стиснення для одного типу файла (розділ 3.1 завдання: за типом обирається стратегія,
    // система визначає, чи стискати файл, і підбирає базові параметри стиснення)
    public class TypeStrategy
    {
        public bool Compress { get; set; } = true; // чи стискати файл цього типу
        public ParserKind Parser { get; set; } = ParserKind.Lazy; // спосіб розбору LZ77
        public int MaxChain { get; set; } = 64; // глибина пошуку повторів
        public int BlockSize { get; set; } = 65536; // розмір блока в байтах

        // Створює стратегію з усіма параметрами
        public TypeStrategy(bool compress, ParserKind parser, int maxChain, int blockSize)
        {
            Compress = compress;
            Parser = parser;
            MaxChain = maxChain;
            BlockSize = blockSize;
        }

        // Початкова таблиця стратегій для типів 0-6. Значення обрано з міркувань нижче,
        // їх варто перевірити командою measure на корпусі і уточнити генетичним алгоритмом
        public static TypeStrategy[] Defaults()
        {
            var strategies = new TypeStrategy[FileClassifier.ClassCount];

            // 0 природний текст: багато коротких повторів слів, лінивий розбір і глибокий пошук знаходять довші збіги
            strategies[0] = new TypeStrategy(true, ParserKind.Lazy, 128, 65536);

            // 1 вихідний код: довгі повтори рядків і назв, теж глибокий пошук
            strategies[1] = new TypeStrategy(true, ParserKind.Lazy, 128, 65536);

            // 2 структуровані дані: повторюються назви полів і роздільники, звичайна глибина
            strategies[2] = new TypeStrategy(true, ParserKind.Lazy, 64, 65536);

            // 3 растр без стиснення: точних повторів мало, глибокий пошук лише витрачає час
            strategies[3] = new TypeStrategy(true, ParserKind.Greedy, 16, 65536);

            // 4 виконуваний файл: повтори є, але короткі, середня глибина
            strategies[4] = new TypeStrategy(true, ParserKind.Lazy, 32, 65536);

            // 5 вже стиснуте: повторів немає, стиснення лише витрачає час
            strategies[5] = new TypeStrategy(false, ParserKind.Lazy, 64, 65536);

            // 6 штучні дані (нулі, повтори): найдовший збіг знаходиться з першого кандидата
            strategies[6] = new TypeStrategy(true, ParserKind.Greedy, 8, 65536);

            return strategies;
        }

        // Повертає копію загальних налаштувань, у якій розбір, глибина пошуку і розмір блока взяті з цієї стратегії
        public Settings ApplyTo(Settings settings)
        {
            Settings copy = settings.Copy();
            copy.Parser = Parser;
            copy.MaxChain = MaxChain;
            copy.BlockSize = BlockSize;
            return copy;
        }

        // Короткий опис стратегії для виведення на екран
        public string Describe()
        {
            if (!Compress)
                return "без стиснення";

            return $"розбір {ParserName(Parser)}, глибина пошуку {MaxChain}, блок {BlockSize} байтів";
        }

        // Назва розбору так, як її пишуть в опції --parser
        static string ParserName(ParserKind parser)
        {
            if (parser == ParserKind.Greedy)
                return "greedy";
            if (parser == ParserKind.Lazy)
                return "lazy";
            return "dp";
        }
    }
}
