namespace SimpleArchiver
{
    // Записує числа у порядку big-endian. Спершу старший байт, потім молодші
    public class ByteWriter
    {
        List<byte> bytes = new List<byte>(); // записані байти

        public int Length => bytes.Count; // скільки байтів уже записано

        // Записує один байт
        public void WriteU8(int value)
        {
            bytes.Add((byte)value);
        }

        // Записує число двома байтами, спершу старший
        public void WriteU16(int value)
        {
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
