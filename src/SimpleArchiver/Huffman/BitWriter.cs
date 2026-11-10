namespace SimpleArchiver
{
    // Записує біти. Перший біт стає старшим бітом першого байта, кінець добивається нулями
    public class BitWriter
    {
        List<byte> bytes = new List<byte>(); // готові повні байти
        int current; // біти поточного байта
        int bitsInCurrent; // скільки бітів уже в current

        // Дописує один біт
        public void WriteBit(int bit)
        {
            current = (current << 1) | (bit & 1);
            bitsInCurrent++;

            if (bitsInCurrent == 8)
            {
                bytes.Add((byte)current);
                current = 0;
                bitsInCurrent = 0;
            }
        }

        // Записує count бітів числа value, починаючи зі старшого
        public void WriteBits(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                WriteBit((value >> i) & 1);
        }

        // Повертає всі байти. Неповний останній байт доповнюється нулями справа
        public byte[] ToArray()
        {
            var result = new List<byte>(bytes);

            if (bitsInCurrent > 0)
                result.Add((byte)(current << (8 - bitsInCurrent)));

            return result.ToArray();
        }
    }
}
