namespace SimpleArchiver
{
    // Читає біти в тому ж порядку, в якому їх записав BitWriter
    public class BitReader
    {
        byte[] data; // байти, з яких читаємо біти
        long position; // номер наступного біта

        // Створює читача бітів
        public BitReader(byte[] data)
        {
            this.data = data;
        }

        long TotalBits => (long)data.Length * 8; // скільки всього бітів у даних

        // Читає один біт
        public int ReadBit()
        {
            if (position >= TotalBits)
                throw new ArchiveException(
                    ErrorCodes.Code,
                    "бітовий потік закінчився раніше, ніж треба"
                );

            int byteIndex = (int)(position / 8);
            int shift = 7 - (int)(position % 8);
            position++;

            return (data[byteIndex] >> shift) & 1;
        }

        // Читає count бітів і складає з них число
        public int ReadBits(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
                value = (value << 1) | ReadBit();

            return value;
        }

        // Перевіряє, що в кінці лишились тільки нулі доповнення, менше 8 бітів
        public void CheckEndIsClean()
        {
            long left = TotalBits - position;

            if (left >= 8)
                throw new ArchiveException(
                    ErrorCodes.Code,
                    "у бітовому потоці лишилися зайві байти"
                );

            for (long i = 0; i < left; i++)
            {
                if (ReadBit() != 0)
                    throw new ArchiveException(
                        ErrorCodes.Code,
                        "доповнення останнього байта не нульове"
                    );
            }
        }
    }
}
