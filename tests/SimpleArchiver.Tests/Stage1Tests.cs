using System.Text;

namespace SimpleArchiver.Tests
{
    public class Stage1Tests
    {
        // Числа записуються старшим байтом уперед
        [Fact]
        public void ByteWriterWritesBigEndian()
        {
            var writer = new ByteWriter();
            writer.WriteU16(0x1234);
            writer.WriteU32(0xA1B2C3D4);

            Assert.Equal(new byte[] { 0x12, 0x34, 0xA1, 0xB2, 0xC3, 0xD4 }, writer.ToArray());

            var reader = new ByteReader(writer.ToArray(), ErrorCodes.Payload);
            Assert.Equal(0x1234, reader.ReadU16());
            Assert.Equal(0xA1B2C3D4u, reader.ReadU32());
            Assert.Equal(0, reader.Remaining);
        }
    }
}
