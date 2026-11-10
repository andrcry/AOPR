namespace SimpleArchiver
{
    // Один файл: ім'я і вміст
    public class ArchiveFile
    {
        public string Name { get; set; } // ім'я файла без шляху
        public byte[] Data { get; set; } // вміст файла

        // Створює файл з ім'ям і вмістом
        public ArchiveFile(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }
    }
}
