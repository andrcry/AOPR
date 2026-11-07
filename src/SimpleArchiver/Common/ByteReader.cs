namespace SimpleArchiver
{
    // Читає big-endian числа по черзі. Якщо байтів не вистачає, кидає помилку з заданим кодом
    public class ByteReader
    {
        byte[] data; // масив, з якого читаємо
        int position; // номер наступного байта для читання
        string errorCode; // код помилки, якщо байтів не вистачило

        // Створює читача для масиву байтів
        public ByteReader(byte[] data, string errorCode)
        {
            this.data = data;
            this.errorCode = errorCode;
        }

        public int Remaining => data.Length - position; // скільки байтів ще не прочитано

        // Перевіряє, що попереду є ще count байтів
        void Need(int count)
        {
            if (count < 0 || count > Remaining)
                throw new ArchiveException(errorCode, "дані закінчилися раніше, ніж очікувалось");
        }

        // Читає один байт
        public int ReadU8()
        {
            Need(1);
            int value = data[position];
            position++;
            return value;
        }

        // Читає число з двох байтів
        public int ReadU16()
        {
            Need(2);
            int value = (data[position] << 8) | data[position + 1];
            position += 2;
            return value;
        }

        // Читає число з чотирьох байтів
        public uint ReadU32()
        {
            Need(4);
            uint value =
                ((uint)data[position] << 24)
                | ((uint)data[position + 1] << 16)
                | ((uint)data[position + 2] << 8)
                | data[position + 3];
            position += 4;
            return value;
        }

        // Читає uint32 і перевіряє, що він вміщується в int
        public int ReadU32AsInt()
        {
            uint value = ReadU32();
            if (value > int.MaxValue)
                throw new ArchiveException(errorCode, "число в архіві завелике");

            return (int)value;
        }

        // Читає count байтів і повертає їх окремим масивом
        public byte[] ReadBytes(int count)
        {
            Need(count);
            var result = new byte[count];
            Array.Copy(data, position, result, 0, count);
            position += count;
            return result;
        }
    }
}
