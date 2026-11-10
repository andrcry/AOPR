using System.Text;

namespace SimpleArchiver.Tests
{
    // Штучні файли різних типів, поки немає справжніх train/ і valid/ від викладача
    public static class SyntheticFiles
    {
        // слова для штучного тексту
        static string[] words =
        {
            "алгоритм",
            "архів",
            "стиснення",
            "дерево",
            "код",
            "файл",
            "блок",
            "the",
            "data",
            "tree",
            "and",
            "compression",
            "is",
            "a",
            "of",
            "методичка",
            "проєкт",
            "студент",
            "текст",
            "слово",
        };

        // Тип 0: природний текст - слова через пробіл, рядки по 60-90 символів
        public static byte[] Text(int size, int seed)
        {
            var random = new Random(seed);
            var text = new StringBuilder();
            int lineLength = 0;

            while (text.Length < size)
            {
                string word = words[random.Next(words.Length)];
                text.Append(word);
                lineLength += word.Length + 1;

                if (lineLength > 60 + random.Next(30))
                {
                    text.Append(".\n");
                    lineLength = 0;
                }
                else
                {
                    text.Append(' ');
                }
            }

            return Cut(Encoding.UTF8.GetBytes(text.ToString()), size);
        }

        // Тип 2: структуровані дані - CSV з числами
        public static byte[] Csv(int size, int seed)
        {
            var random = new Random(seed);
            var text = new StringBuilder("id,name,value,date\n");
            int id = 1;

            while (text.Length < size)
            {
                text.Append(
                    $"{id},item{random.Next(100)},{random.Next(100000)},2026-{random.Next(1, 13):D2}-{random.Next(1, 29):D2}\n"
                );
                id++;
            }

            return Cut(Encoding.ASCII.GetBytes(text.ToString()), size);
        }

        // Тип 6: штучні дані - нулі або один байт, що повторюється
        public static byte[] Synthetic(int size, int seed)
        {
            var data = new byte[size];
            byte value = (byte)(seed % 3 == 0 ? 0 : new Random(seed).Next(256));
            for (int i = 0; i < size; i++)
                data[i] = value;
            return data;
        }

        // Тип 5: вже стиснуте - випадковий шум
        public static byte[] Compressed(int size, int seed) => TestHelpers.RandomBytes(size, seed);

        // count прикладів кожного з чотирьох типів (0, 2, 5, 6)
        public static List<Sample> MakeSamples(int count, int firstSeed)
        {
            var samples = new List<Sample>();

            for (int k = 0; k < count; k++)
            {
                int seed = firstSeed + k;
                int size = 2000 + (seed * 37) % 3000;

                samples.Add(new Sample($"text{seed}", FileFeatures.Compute(Text(size, seed)), 0));
                samples.Add(new Sample($"csv{seed}", FileFeatures.Compute(Csv(size, seed)), 2));
                samples.Add(
                    new Sample($"zip{seed}", FileFeatures.Compute(Compressed(size, seed)), 5)
                );
                samples.Add(
                    new Sample($"zero{seed}", FileFeatures.Compute(Synthetic(size, seed)), 6)
                );
            }

            return samples;
        }

        // Навчає класифікатор за протоколом: train, valid і три ініціалізації
        public static FileClassifier TrainClassifier()
        {
            List<Sample> train = MakeSamples(15, 100);
            List<Sample> valid = MakeSamples(5, 500);
            return FileClassifier.TrainBest(train, valid, new[] { 1, 2, 3 }, 150, 0.5, false);
        }

        // Обрізає масив до потрібного розміру
        static byte[] Cut(byte[] data, int size)
        {
            var result = new byte[Math.Min(size, data.Length)];
            Array.Copy(data, result, result.Length);
            return result;
        }
    }
}
