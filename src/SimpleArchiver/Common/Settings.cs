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
        public bool UseTypeStrategy { get; set; } = false; // true - параметри стиснення за типом файла (потрібен класифікатор)
        public TypeStrategy[] TypeStrategies { get; set; } = TypeStrategy.Defaults(); // параметри стиснення для типів 0-6

        // Томи
        public int MaxVolumeBytes { get; set; } = 1048576; // max_volume_bytes
        public bool UseDpKnapsack { get; set; } = true; // true - рюкзак ДП, false - жадібний
        public int[] TypePriorities { get; set; } = { 1, 1, 1, 1, 1, 1, 1 }; // пріоритети_типів 0-6

        // Захист від аналізу метаданих
        public int PadUnit { get; set; } = 0; // вирівнювати навантаження тому до кратного цьому числу байтів, 0 - не вирівнювати
        public bool PadToPowerOfTwo { get; set; } = false; // true - вирівнювати навантаження тому до степеня двійки

        // Повертає копію налаштувань, щоб змінити її для одного файла, не чіпаючи загальні
        public Settings Copy()
        {
            return (Settings)MemberwiseClone();
        }

        // Повертає цінність блока за типом файла. Не менше 1
        public int GetPriority(int fileType)
        {
            if (fileType < 0 || fileType >= TypePriorities.Length)
                return 1;

            return Math.Max(1, TypePriorities[fileType]);
        }
    }
}
