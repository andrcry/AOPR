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

        // Обрізає масив до потрібного розміру
        static byte[] Cut(byte[] data, int size)
        {
            var result = new byte[Math.Min(size, data.Length)];
            Array.Copy(data, result, result.Length);
            return result;
        }
    }
}
