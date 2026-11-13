using System.Text.Json;

namespace SimpleArchiver
{
    // Параметри команди з файла data/team_config.json від викладача.
    // Значення за замовчуванням тут такі самі, як у файлі команди T01, тому програма працює і без файла
    public class TeamConfig
    {
        public const string DefaultPath = "data/team_config.json"; // де лежить файл, якщо запускати програму з папки проєкту

        public string Team { get; set; } = "T01"; // назва команди
        public int Seed { get; set; } = 2001; // seed, з яким викладач згенерував корпус команди
        public string Password { get; set; } = "algo2026"; // пароль_сеансу: пароль еталонних архівів

        // Метрика Q = alpha * S + beta * T
        public double Alpha { get; set; } = 0.7; // вага стиснення S
        public double Beta { get; set; } = 0.3; // вага швидкості T
        public double TRef { get; set; } = 30.0; // t_ref у секундах

        // Пакування
        public int MaxVolumeBytes { get; set; } = 65536; // найбільший розмір тому разом із заголовком і HMAC
        public int BlockSize { get; set; } = 16384; // найбільший розмір блока
        public int[] TypePriorities { get; set; } = { 3, 3, 2, 2, 2, 1, 1 }; // цінність блока для рюкзака за типом 0-6

        // Фіксовані вартості ДП-розбору
        public int LiteralBits { get; set; } = 9; // вартість літерала, біт
        public int ReferenceBits { get; set; } = 17; // базова вартість посилання, біт

        // Класифікатор
        public double F1Min { get; set; } = 0.61; // найменший допустимий macro-F1
        public double BaselineMajority { get; set; } = 0.0357; // macro-F1, якщо завжди відповідати одним класом
        public double BaselineRandom { get; set; } = 0.1614; // macro-F1 випадкового вибору
        public int Initializations { get; set; } = 3; // скільки разів навчати мережу з різних початкових ваг

        // Довідково: результат опорного пакувальника викладача на корпусі
        public long CorpusBytes { get; set; } = 329903; // сирий розмір корпусу
        public long ReferenceArchiveBytes { get; set; } = 128968; // розмір архіву опорного пакувальника

        // Читає файл team_config.json
        public static TeamConfig Load(string path)
        {
            var config = new TeamConfig();

            try
            {
                using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = json.RootElement;

                // Крок 1. Команда, seed і пароль еталонних архівів
                config.Team = root.GetProperty("команда").GetString() ?? "";
                config.Seed = root.GetProperty("seed").GetInt32();
                config.Password = root.GetProperty("пароль_сеансу").GetString() ?? "";

                // Крок 2. Метрика Q
                JsonElement q = root.GetProperty("метрика_Q");
                config.Alpha = q.GetProperty("alpha").GetDouble();
                config.Beta = q.GetProperty("beta").GetDouble();
                config.TRef = q.GetProperty("t_ref_секунд").GetDouble();

                // Крок 3. Розмір тому і блока
                JsonElement packing = root.GetProperty("пакування");
                config.MaxVolumeBytes = packing.GetProperty("max_volume_bytes").GetInt32();
                config.BlockSize = packing.GetProperty("block_size").GetInt32();

                // Крок 4. Пріоритети записані за назвою типу, наприклад "природний текст": 3
                JsonElement priorities = root.GetProperty("пріоритети_типів");
                config.TypePriorities = new int[FileClassifier.ClassCount];
                for (int type = 0; type < FileClassifier.ClassCount; type++)
                    config.TypePriorities[type] = priorities
                        .GetProperty(FileClassifier.classNames[type])
                        .GetInt32();

                // Крок 5. Фіксовані вартості ДП
                JsonElement costs = root.GetProperty("фіксовані_вартості_ДП");
                config.LiteralBits = costs.GetProperty("літерал_біт").GetInt32();
                config.ReferenceBits = costs.GetProperty("посилання_база_біт").GetInt32();

                // Крок 6. Класифікатор
                JsonElement classifier = root.GetProperty("класифікатор");
                config.F1Min = classifier.GetProperty("F1_min").GetDouble();
                config.BaselineMajority = classifier
                    .GetProperty("базова_лінія_мажоритарний_клас")
                    .GetDouble();
                config.BaselineRandom = classifier
                    .GetProperty("базова_лінія_випадковий_вибір")
                    .GetDouble();
                config.Initializations = classifier
                    .GetProperty("протокол_навчання")
                    .GetProperty("ініціалізацій")
                    .GetInt32();

                // Крок 7. Довідкові числа
                JsonElement reference = root.GetProperty("довідково");
                config.CorpusBytes = reference.GetProperty("сирий_розмір").GetInt64();
                config.ReferenceArchiveBytes = reference
                    .GetProperty("розмір_архіву_опорного_пакувальника")
                    .GetInt64();
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException(
                    $"файл {path} - неправильний JSON: {e.Message}"
                );
            }
            catch (KeyNotFoundException e)
            {
                throw new InvalidOperationException(
                    $"у файлі {path} немає потрібного поля: {e.Message}"
                );
            }

            return config;
        }

        // Читає файл, якщо він є. Якщо файла немає, повертає значення за замовчуванням
        public static TeamConfig LoadIfExists(string path)
        {
            if (File.Exists(path))
                return Load(path);

            return new TeamConfig();
        }

        // Робить розмір блока, розмір тому і пріоритети стандартними для всіх нових налаштувань програми
        public void Apply()
        {
            Settings.teamBlockSize = BlockSize;
            Settings.teamMaxVolumeBytes = MaxVolumeBytes;
            Settings.teamPriorities = (int[])TypePriorities.Clone();
        }

        // Рядки для команди team-config: що прочитано і чи збігається з програмою
        public List<string> Describe()
        {
            var lines = new List<string>();
            lines.Add($"Команда {Team}, seed {Seed}, пароль еталонних архівів {Password}");
            lines.Add($"Метрика Q: alpha = {Alpha}, beta = {Beta}, t_ref = {TRef} с");
            lines.Add($"Том до {MaxVolumeBytes} байтів, блок до {BlockSize} байтів");

            string priorities = "";
            for (int type = 0; type < TypePriorities.Length; type++)
                priorities += $"{FileClassifier.classNames[type]} {TypePriorities[type]}; ";
            lines.Add($"Пріоритети типів: {priorities}");

            bool sameCosts = LiteralBits == Lz77.LiteralCost && ReferenceBits == Lz77.MatchCost(1);
            lines.Add(
                $"Вартості ДП: літерал {LiteralBits} біт, посилання {ReferenceBits} біт + додаткові. "
                    + (
                        sameCosts
                            ? "Збігаються з програмою"
                            : "НЕ збігаються з програмою (Lz77.LiteralCost, Lz77.MatchCost)"
                    )
            );

            lines.Add(
                $"Класифікатор: F1_min = {F1Min}, ініціалізацій {Initializations}, "
                    + $"базові лінії {BaselineMajority} (один клас) і {BaselineRandom} (випадково)"
            );

            double ratio = (double)CorpusBytes / ReferenceArchiveBytes;
            lines.Add(
                $"Опорний пакувальник: корпус {CorpusBytes} байтів, архів {ReferenceArchiveBytes} байтів, коефіцієнт {ratio:F3}"
            );
            return lines;
        }
    }
}
