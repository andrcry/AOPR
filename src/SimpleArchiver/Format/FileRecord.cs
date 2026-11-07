using System.Text;

namespace SimpleArchiver
{
    // Запис таблиці файлів: ім'я, розмір і CRC32 цілого файла, тип, стратегія
    public class FileRecord
    {
        public string Name { get; set; } = ""; // ім'я файла
        public int OriginalSize { get; set; } // розмір цілого файла
        public uint Crc { get; set; } // CRC32 цілого файла
        public int FileType { get; set; } // 0-6, поки завжди 0
        public int Strategy { get; set; } // 1 - блоки закодовано Гаффманом

        // Скільки байтів займає запис у таблиці файлів
        public int SizeInTable() => 2 + Encoding.UTF8.GetByteCount(Name) + 4 + 4 + 1 + 1;
    }
}
