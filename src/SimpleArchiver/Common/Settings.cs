namespace SimpleArchiver
{
    // Налаштування архіватора. Справжні значення взяти з team_config.json
    public class Settings
    {
        // Розбиття на блоки
        public int BlockSize { get; set; } = 65536; // block_size: найбільший розмір блока

        // LZ77
        public ParserKind Parser { get; set; } = ParserKind.Lazy;
        public int MaxChain { get; set; } = 64; // скільки кандидатів перевіряти при пошуку збігу
    }
}
