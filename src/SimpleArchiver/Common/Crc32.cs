namespace SimpleArchiver
{
    // Контрольна сума CRC32, як у ZIP
    public static class Crc32
    {
        static uint[] table = MakeTable(); // таблиця з 256 значень для швидкого підрахунку

        // Рахує таблицю з 256 значень один раз, щоб потім рахувати швидко
        static uint[] MakeTable()
        {
            var result = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;

                result[n] = c;
            }

            return result;
        }

        // Рахує CRC32 для масиву байтів
        public static uint Compute(byte[] data)
        {
            uint crc = 0xFFFFFFFF;

            foreach (byte b in data)
                crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFF;
        }
    }
}
