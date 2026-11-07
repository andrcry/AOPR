namespace SimpleArchiver
{
    // Перевірка імен файлів. Ім'я на кшталт ../secret записало б файл поза папкою розпакування
    public static class SafeNames
    {
        const string forbidden = "/\\:\0"; // символи, заборонені в імені файла

        // Перевіряє, що ім'я не веде за межі папки розпакування
        public static bool IsSafe(string name)
        {
            if (name.Length == 0 || name == "." || name == "..")
                return false;

            foreach (char symbol in name)
            {
                if (forbidden.Contains(symbol))
                    return false;
            }

            return !Path.IsPathRooted(name);
        }
    }
}
