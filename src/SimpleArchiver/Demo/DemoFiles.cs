using System.Text;

namespace SimpleArchiver
{
    // Штучні файли семи груп корпусу для демонстрації, поки немає справжніх файлів викладача.
    // Кожен файл залежить лише від seed: з тим самим seed виходить той самий файл
    public static class DemoFiles
    {
        // Назви груп англійською для імен файлів і папок корпусу, номер групи - тип 0-6
        public static readonly string[] groupNames =
        {
            "text",
            "code",
            "structured",
            "raster",
            "executable",
            "compressed",
            "synthetic",
        };

        // Розширення файлів кожної групи
        static string[] extensions = { ".txt", ".c", ".csv", ".bmp", ".exe", ".jpg", ".bin" };

        // Слова для природного тексту
        static string[] words =
        {
            "алгоритм",
            "архів",
            "стиснення",
            "дерево",
            "код",
            "файл",
            "блок",
            "студент",
            "текст",
            "слово",
            "мова",
            "книга",
            "розділ",
            "задача",
            "рішення",
            "the",
            "data",
            "tree",
            "and",
            "compression",
            "is",
            "a",
            "of",
            "to",
            "in",
            "file",
            "block",
            "student",
            "book",
            "chapter",
        };

        // Імена змінних і функцій для вихідного коду
        static string[] names =
        {
            "count",
            "index",
            "value",
            "buffer",
            "length",
            "result",
            "node",
            "left",
            "right",
            "total",
        };

        // Ім'я файла: група, номер, розширення групи.
        // Структуровані дані бувають CSV, JSON або лог, тому їхнє розширення залежить від seed, як і вміст
        public static string FileName(int group, int number, int seed)
        {
            string extension = extensions[group];
            if (group == 2 && seed % 3 == 1)
                extension = ".json";
            if (group == 2 && seed % 3 == 2)
                extension = ".log";

            return $"{groupNames[group]}_{number:D3}{extension}";
        }

        // Створює файл потрібної групи приблизно заданого розміру
        public static byte[] Make(int group, int size, int seed)
        {
            if (group == 0)
                return Text(size, seed);
            if (group == 1)
                return Code(size, seed);
            if (group == 2)
                return Structured(size, seed);
            if (group == 3)
                return Raster(size, seed);
            if (group == 4)
                return Executable(size, seed);
            if (group == 5)
                return Compressed(size, seed);
            return Synthetic(size, seed);
        }

        // Група 0. Природний текст: речення з випадкових слів, рядки по 60-90 символів
        public static byte[] Text(int size, int seed)
        {
            var random = new Random(seed);
            var text = new StringBuilder();
            int lineLength = 0;

            while (text.Length < size)
            {
                // Речення з 5-12 слів
                int count = 5 + random.Next(8);
                for (int i = 0; i < count; i++)
                {
                    string word = words[random.Next(words.Length)];
                    if (i == 0)
                        word = char.ToUpper(word[0]) + word.Substring(1);

                    text.Append(word);
                    text.Append(i == count - 1 ? ". " : " ");
                    lineLength += word.Length + 1;
                }

                // Новий рядок, коли рядок задовгий
                if (lineLength > 60 + random.Next(30))
                {
                    text.Append('\n');
                    lineLength = 0;
                }
            }

            return Cut(Encoding.UTF8.GetBytes(text.ToString()), size);
        }

        // Група 1. Вихідний код мовою C: схожі функції з різними іменами і числами
        public static byte[] Code(int size, int seed)
        {
            var random = new Random(seed);
            var text = new StringBuilder("#include <stdio.h>\n#include <stdlib.h>\n\n");
            int number = 0;

            while (text.Length < size)
            {
                string name = names[random.Next(names.Length)];
                string other = names[random.Next(names.Length)];
                int limit = random.Next(2, 1000);

                text.Append($"/* Function {number}: works with {name} */\n");
                text.Append($"int {name}_{number}(int *{other}, int length)\n{{\n");
                text.Append($"    int {name} = 0;\n");
                text.Append($"    for (int i = 0; i < length && i < {limit}; i++)\n    {{\n");
                text.Append($"        if ({other}[i] > {random.Next(100)})\n");
                text.Append($"            {name} += {other}[i] * {random.Next(1, 10)};\n");
                text.Append("        else\n");
                text.Append($"            {name} -= {other}[i];\n");
                text.Append("    }\n");
                text.Append($"    return {name};\n}}\n\n");
                number++;
            }

            return Cut(Encoding.ASCII.GetBytes(text.ToString()), size);
        }

        // Група 2. Структуровані дані: CSV, JSON або лог, залежно від seed
        public static byte[] Structured(int size, int seed)
        {
            var random = new Random(seed);
            var text = new StringBuilder();
            int kind = seed % 3;
            int id = 1;

            if (kind == 0)
                text.Append("id,name,value,date\n");
            if (kind == 1)
                text.Append("[\n");

            while (text.Length < size)
            {
                int month = random.Next(1, 13);
                int day = random.Next(1, 29);

                if (kind == 0)
                    text.Append(
                        $"{id},item{random.Next(100)},{random.Next(100000)},2026-{month:D2}-{day:D2}\n"
                    );
                else if (kind == 1)
                    text.Append(
                        $"  {{\"id\": {id}, \"name\": \"item{random.Next(100)}\", \"value\": {random.Next(100000)}, \"active\": {(random.Next(2) == 0 ? "true" : "false")}}},\n"
                    );
                else
                    text.Append(
                        $"2026-{month:D2}-{day:D2} {random.Next(24):D2}:{random.Next(60):D2}:{random.Next(60):D2} INFO request {id} from 10.0.{random.Next(256)}.{random.Next(256)} took {random.Next(1000)} ms\n"
                    );

                id++;
            }

            return Cut(Encoding.ASCII.GetBytes(text.ToString()), size);
        }

        // Група 3. Растр без стиснення: картинка BMP 24 біти з плавними переходами кольору.
        // Сусідні пікселі майже однакові - саме це має помітити прогноз байта
        public static byte[] Raster(int size, int seed)
        {
            var random = new Random(seed);

            // Розмір картинки: квадрат, щоб файл був близько size байтів
            int width = Math.Max(4, (int)Math.Sqrt(size / 3.0));
            int height = width;
            int rowSize = (width * 3 + 3) / 4 * 4; // рядок BMP вирівнюється до 4 байтів
            int dataSize = rowSize * height;

            var writer = new ByteWriterLittle(54 + dataSize);

            // Заголовок BMP (14 байтів) і заголовок картинки (40 байтів)
            writer.Byte((byte)'B');
            writer.Byte((byte)'M');
            writer.Int(54 + dataSize);
            writer.Int(0);
            writer.Int(54);
            writer.Int(40);
            writer.Int(width);
            writer.Int(height);
            writer.Short(1);
            writer.Short(24);
            writer.Int(0);
            writer.Int(dataSize);
            writer.Int(2835);
            writer.Int(2835);
            writer.Int(0);
            writer.Int(0);

            // Пікселі: градієнт плюс трохи шуму
            int shift = random.Next(256);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    writer.Byte((byte)(x * 255 / width + random.Next(3)));
                    writer.Byte((byte)(y * 255 / height + random.Next(3)));
                    writer.Byte((byte)((x + y + shift) % 256));
                }
                for (int p = width * 3; p < rowSize; p++)
                    writer.Byte(0);
            }

            return writer.ToArray();
        }

        // Група 4. Виконуваний файл: заголовок MZ, ділянки коду з частими командами, нулі, рядки
        public static byte[] Executable(int size, int seed)
        {
            var random = new Random(seed);
            var data = new byte[size];

            // Часті байти машинного коду x86: push, mov, call, ret, nop, jmp та інші
            byte[] opcodes =
            {
                0x55,
                0x89,
                0xE5,
                0x8B,
                0x45,
                0x48,
                0x83,
                0xEC,
                0xC3,
                0xE8,
                0x90,
                0x74,
                0x75,
                0xEB,
                0x31,
                0xC0,
                0xFF,
                0x0F,
            };

            // Заголовок виконуваного файла Windows починається з літер MZ
            data[0] = (byte)'M';
            data[1] = (byte)'Z';
            data[2] = 0x90;

            // Далі випадкові ділянки: машинний код, нулі вирівнювання або таблиця рядків
            int position = 64;
            while (position < size)
            {
                int kind = random.Next(10);
                int length = 64 + random.Next(512);

                for (int i = 0; i < length && position < size; i++)
                {
                    if (kind < 6)
                        data[position] =
                            random.Next(4) == 0
                                ? (byte)random.Next(256)
                                : opcodes[random.Next(opcodes.Length)];
                    else if (kind < 8)
                        data[position] = 0;
                    else
                        data[position] = (byte)(i % 12 == 11 ? 0 : 'a' + random.Next(26));

                    position++;
                }
            }

            return data;
        }

        // Група 5. Вже стиснуте: випадкові байти з заголовком JPEG. Стиснуті дані схожі на шум
        public static byte[] Compressed(int size, int seed)
        {
            var random = new Random(seed);
            var data = new byte[size];
            random.NextBytes(data);

            byte[] header =
            {
                0xFF,
                0xD8,
                0xFF,
                0xE0,
                0x00,
                0x10,
                (byte)'J',
                (byte)'F',
                (byte)'I',
                (byte)'F',
                0x00,
            };
            for (int i = 0; i < header.Length && i < size; i++)
                data[i] = header[i];

            return data;
        }

        // Група 6. Штучні дані: нулі, один байт, що повторюється, або короткий візерунок
        public static byte[] Synthetic(int size, int seed)
        {
            var random = new Random(seed);
            var data = new byte[size];
            int kind = seed % 3;

            if (kind == 1)
            {
                byte value = (byte)random.Next(1, 256);
                for (int i = 0; i < size; i++)
                    data[i] = value;
            }
            else if (kind == 2)
            {
                var pattern = new byte[1 + random.Next(8)];
                random.NextBytes(pattern);
                for (int i = 0; i < size; i++)
                    data[i] = pattern[i % pattern.Length];
            }

            return data;
        }

        // Обрізає масив до потрібного розміру
        static byte[] Cut(byte[] data, int size)
        {
            var result = new byte[Math.Min(size, data.Length)];
            Array.Copy(data, result, result.Length);
            return result;
        }

        // Записує числа молодшим байтом уперед, як вимагає формат BMP
        class ByteWriterLittle
        {
            byte[] bytes; // готовий масив
            int position; // куди писати наступний байт

            // Створює масив потрібної довжини
            public ByteWriterLittle(int length)
            {
                bytes = new byte[length];
            }

            // Записує один байт
            public void Byte(byte value)
            {
                bytes[position] = value;
                position++;
            }

            // Записує 2 байти
            public void Short(int value)
            {
                Byte((byte)value);
                Byte((byte)(value >> 8));
            }

            // Записує 4 байти
            public void Int(int value)
            {
                Short(value);
                Short(value >> 16);
            }

            // Повертає готовий масив
            public byte[] ToArray() => bytes;
        }
    }
}
