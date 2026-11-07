namespace SimpleArchiver
{
    // Запис таблиці блоків разом із даними блока
    public class BlockRecord
    {
        public const int NoCompressionFlag = 0x01; // прапорець блок записано без стиснення
        public const int SizeInTable = 15; // розмір запису в таблиці блоків, байтів

        public int FileIndex { get; set; } // номер файла в таблиці цього тому
        public int BlockNumber { get; set; } // block_seq, від 0
        public int RawLength { get; set; } // розмір блока до стиснення
        public int Flags { get; set; } // прапорці блока
        public byte[] Data { get; set; } = new byte[0]; // дані блока, стиснуті або сирі

        public bool IsNotCompressed => (Flags & NoCompressionFlag) != 0; // true, якщо блок записано без стиснення
    }
}
