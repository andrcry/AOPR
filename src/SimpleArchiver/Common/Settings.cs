namespace SimpleArchiver
{
    // Налаштування архіватора. Справжні значення взяти з team_config.json
    public class Settings
    {
        // Розбиття на блоки
        public int BlockSize { get; set; } = 65536; // block_size: найбільший розмір блока
        public bool UseDpBlockSplit { get; set; } = false; // true - ДП-розбиття на блоки (просунутий рівень)
        public int SplitUnit { get; set; } = 8192; // крок, з яким ДП шукає межі блоків

        // LZ77
        public ParserKind Parser { get; set; } = ParserKind.Lazy;
        public int MaxChain { get; set; } = 64; // скільки кандидатів перевіряти при пошуку збігу

        // Аналіз файлу
        public string? ClassifierPath { get; set; } // файл з вагами перцептрона, null - не класифікувати
        public bool UsePrediction { get; set; } = false; // прогноз наступного байта (командне розширення)
        public int HistoryLength { get; set; } = 4; // скільки попередніх байтів бачить прогноз

        // Томи
        public int MaxVolumeBytes { get; set; } = 1048576; // max_volume_bytes
        public bool UseDpKnapsack { get; set; } = true; // true - рюкзак ДП, false - жадібний
        public int[] TypePriorities { get; set; } = { 1, 1, 1, 1, 1, 1, 1 }; // пріоритети_типів 0-6

        // Повертає цінність блока за типом файла. Не менше 1
        public int GetPriority(int fileType)
        {
            if (fileType < 0 || fileType >= TypePriorities.Length)
                return 1;

            return Math.Max(1, TypePriorities[fileType]);
        }
    }
}
