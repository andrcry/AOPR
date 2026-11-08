namespace SimpleArchiver
{
    // Закодований блок, який ще не покладено в том. FileNumber - номер файла в усьому архіві
    public class PreparedBlock
    {
        public int FileNumber { get; set; } // номер файла в архіві
        public int BlockNumber { get; set; } // номер блока у файлі
        public int RawLength { get; set; } // розмір блока до стиснення
        public int Flags { get; set; } // прапорці блока
        public byte[] Data { get; set; } = new byte[0]; // дані блока, стиснуті або сирі

        // Вага для рюкзака: закодовані байти + 15 байтів запису в таблиці блоків
        public int Weight() => Data.Length + BlockRecord.SizeInTable;
    }
}
