namespace SimpleArchiver
{
    // Налаштування архіватора. Розмір блока, розмір тому і пріоритети типів беруться з team_config.json
    public class Settings
    {
        // Значення з team_config.json. Тут записані значення команди T01, а програма на старті
        // читає файл і замінює їх (TeamConfig.Apply). Кожні нові налаштування беруть ці значення
        public static int teamBlockSize = 16384; // block_size
        public static int teamMaxVolumeBytes = 65536; // max_volume_bytes
        public static int[] teamPriorities = { 3, 3, 2, 2, 2, 1, 1 }; // пріоритети_типів 0-6

        // Розбиття на блоки
        public int BlockSize { get; set; } = teamBlockSize; // найбільший розмір блока
        public bool UseDpBlockSplit { get; set; } = false; // true - ДП-розбиття на блоки (просунутий рівень)
        public int SplitUnit { get; set; } = 2048; // крок, з яким ДП шукає межі блоків, BlockSize має ділитися на нього

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
        public int MaxVolumeBytes { get; set; } = teamMaxVolumeBytes; // найбільший розмір тому
        public bool UseDpKnapsack { get; set; } = true; // true - рюкзак ДП, false - жадібний
        public int[] TypePriorities { get; set; } = (int[])teamPriorities.Clone(); // цінність блока за типом 0-6

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
