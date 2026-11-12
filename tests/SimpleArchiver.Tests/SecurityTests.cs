namespace SimpleArchiver.Tests
{
    // Захист: вирівнювання розміру тому
    public class SecurityTests
    {
        // Довжина навантаження тому: розмір тому мінус заголовок 41 байт і пломба 32 байти
        static int PayloadLength(byte[] volume) =>
            volume.Length - VolumeHeader.Size - VolumeHeader.MacSize;

        // Вирівнювання до 4096 байтів: навантаження кратне 4096, файли відновлюються
        [Fact]
        public void PaddingRoundsVolumeToUnit()
        {
            var settings = new Settings();
            settings.PadUnit = 4096;

            byte[] text = SyntheticFiles.Text(5000, 70);
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("a.txt", text),
                TestHelpers.Password,
                settings
            );

            Assert.Equal(0, PayloadLength(volumes[0]) % 4096);
            Assert.Equal(text, ArchiveReader.ReadArchive(volumes, TestHelpers.Password)[0].Data);
        }

        // Вирівнювання до степеня двійки: довжина навантаження - степінь двійки
        [Fact]
        public void PaddingToPowerOfTwo()
        {
            var settings = new Settings();
            settings.PadToPowerOfTwo = true;

            byte[] noise = TestHelpers.RandomBytes(3000, 71);
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("a.bin", noise),
                TestHelpers.Password,
                settings
            );

            int length = PayloadLength(volumes[0]);
            Assert.Equal(4096, length);
            Assert.Equal(noise, ArchiveReader.ReadArchive(volumes, TestHelpers.Password)[0].Data);
        }

        // Вирівнювання не робить том більшим за дозволений розмір
        [Fact]
        public void PaddingNeverExceedsVolumeLimit()
        {
            var settings = new Settings();
            settings.BlockSize = 1000;
            settings.MaxVolumeBytes = 3000;
            settings.PadUnit = 4096;

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("noise.bin", TestHelpers.RandomBytes(10000, 72)),
                TestHelpers.Password,
                settings
            );

            foreach (byte[] volume in volumes)
                Assert.True(volume.Length <= 3000);
            Assert.Equal(
                TestHelpers.RandomBytes(10000, 72),
                ArchiveReader.ReadArchive(volumes, TestHelpers.Password)[0].Data
            );
        }

        // Ненульові байти вирівнювання відхиляються
        [Fact]
        public void PaddingBytesMustBeZero()
        {
            var payload = new Payload();
            payload.Flags = Payload.PaddingFlag;
            payload.PaddingLength = 10;

            byte[] bytes = payload.ToBytes();
            bytes[bytes.Length - 1] = 1;

            byte[] volume = TestHelpers.SealPayload(bytes);
            Assert.Equal(
                ErrorCodes.Payload,
                TestHelpers.ErrorWhenReading(volume, TestHelpers.Password)
            );
        }
    }
}
