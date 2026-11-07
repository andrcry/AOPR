using System.Text;

namespace SimpleArchiver.Tests
{
    // Допоміжні методи для тестів
    public static class TestHelpers
    {
        // Файл з контрольного прикладу: 40 повторів "hello world " = 480 байтів
        public static byte[] Hello480()
        {
            string text = "";
            for (int i = 0; i < 40; i++)
                text += "hello world ";

            return Encoding.ASCII.GetBytes(text);
        }
    }
}
