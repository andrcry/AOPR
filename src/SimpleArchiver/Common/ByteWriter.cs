namespace SimpleArchiver
{
    // Записує числа у порядку big-endian. Спершу старший байт, потім молодші
    public class ByteWriter
    {
        List<byte> bytes = new List<byte>(); // записані байти

        public int Length => bytes.Count; // скільки байтів уже записано

        // Записує один байт. Число поза 0…255 не обрізається мовчки, а дає помилку
        public void WriteU8(int value)
        {
            if (value < 0 || value > 0xFF)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "uint8 має бути в межах 0…255"
                );

            bytes.Add((byte)value);
        }

        // Записує число двома байтами, спершу старший. Число поза 0…65535 дає помилку
        public void WriteU16(int value)
        {
            if (value < 0 || value > 0xFFFF)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "uint16 має бути в межах 0…65535"
                );

            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        // Записує число чотирма байтами, спершу старший
        public void WriteU32(uint value)
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        // Дописує масив байтів у кінець
        public void WriteBytes(byte[] data)
        {
            bytes.AddRange(data);
        }

        // Повертає всі записані байти
        public byte[] ToArray()
        {
            return bytes.ToArray();
        }
    }
}
