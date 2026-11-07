using System.Text;

namespace SimpleArchiver.Tests
{
    // Допоміжні методи для тестів
    public static class TestHelpers
    {
        public const string Password = "algo2026"; // пароль для тестів

        // Звичайні налаштування
        public static Settings DefaultSettings() => new Settings();

        // Файл з контрольного прикладу: 40 повторів "hello world " = 480 байтів
        public static byte[] Hello480()
        {
            string text = "";
            for (int i = 0; i < 40; i++)
                text += "hello world ";

            return Encoding.ASCII.GetBytes(text);
        }

        // Випадкові байти з фіксованим seed, щоб тест завжди був однаковий
        public static byte[] RandomBytes(int count, int seed)
        {
            var data = new byte[count];
            new Random(seed).NextBytes(data);
            return data;
        }

        // Список з одного файла
        public static List<ArchiveFile> OneFile(string name, byte[] data)
        {
            var files = new List<ArchiveFile>();
            files.Add(new ArchiveFile(name, data));
            return files;
        }

        // Пробує прочитати архів. Повертає код помилки або "OK"
        public static string ErrorWhenReading(List<byte[]> volumes, string password)
        {
            try
            {
                ArchiveReader.ReadArchive(volumes, password);
            }
            catch (ArchiveException e)
            {
                return e.Code;
            }

            return "OK";
        }

        // Пробує прочитати один том і повертає код помилки
        public static string ErrorWhenReading(byte[] volume, string password)
        {
            var volumes = new List<byte[]>();
            volumes.Add(volume);
            return ErrorWhenReading(volumes, password);
        }

        // Запечатує власноруч складене навантаження як справжній том
        public static byte[] SealPayload(byte[] payload)
        {
            var salt = new byte[16];
            var nonce = new byte[12];
            return Volume.Seal(0, 1, salt, nonce, payload, Keys.Derive(Password, salt));
        }

        // Копія масиву
        public static byte[] Copy(byte[] source) => (byte[])source.Clone();
    }
}
