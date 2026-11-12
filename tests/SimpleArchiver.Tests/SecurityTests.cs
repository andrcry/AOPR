using System.Numerics;
using System.Text;

namespace SimpleArchiver.Tests
{
    // Захист: вирівнювання розміру тому, аналіз витоку метаданих, Діффі - Геллман
    public class SecurityTests
    {
        // Довжина навантаження тому: розмір тому мінус заголовок 41 байт і пломба 32 байти
        static int PayloadLength(byte[] volume) =>
            volume.Length - VolumeHeader.Size - VolumeHeader.MacSize;

        // Створює порожню тимчасову папку для тесту
        static string MakeTempFolder()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "sarc-test-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(folder);
            return folder;
        }

        // Перевірка Міллера - Рабіна: чи схоже число на просте
        static bool IsProbablePrime(BigInteger n)
        {
            BigInteger d = n - 1;
            int r = 0;
            while (d % 2 == 0)
            {
                d /= 2;
                r++;
            }

            int[] bases = { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29 };
            foreach (int a in bases)
            {
                BigInteger x = BigInteger.ModPow(a, d, n);
                if (x == 1 || x == n - 1)
                    continue;

                bool composite = true;
                for (int i = 0; i < r - 1; i++)
                {
                    x = BigInteger.ModPow(x, 2, n);
                    if (x == n - 1)
                    {
                        composite = false;
                        break;
                    }
                }

                if (composite)
                    return false;
            }

            return true;
        }

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

        // Без вирівнювання кожен файл впізнається за розміром, з вирівнюванням розміри однакові
        [Fact]
        public void LeakTablePaddingHidesSizes()
        {
            string folder = MakeTempFolder();
            try
            {
                File.WriteAllBytes(
                    Path.Combine(folder, "a.bin"),
                    TestHelpers.RandomBytes(1000, 73)
                );
                File.WriteAllBytes(
                    Path.Combine(folder, "b.bin"),
                    TestHelpers.RandomBytes(1500, 74)
                );
                File.WriteAllBytes(
                    Path.Combine(folder, "c.bin"),
                    TestHelpers.RandomBytes(2000, 75)
                );

                List<string> lines = LeakTable.Build(
                    new string[] { "leak-table", "--pad", "4096", folder },
                    1
                );

                // Рядки порівняння: спосіб, різних розмірів, впізнаних файлів, витік бітів, додано байтів, відсотків
                string[] plain = lines[lines.Count - 3].Split('\t');
                string[] byUnit = lines[lines.Count - 2].Split('\t');

                Assert.Equal("3", plain[1]);
                Assert.Equal("3", plain[2]);
                Assert.Equal("0", plain[4]);
                Assert.Equal("1", byUnit[1]);
                Assert.Equal("0", byUnit[2]);
                Assert.True(long.Parse(byUnit[4]) > 0);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // Модуль Діффі - Геллмана - просте число на 2048 бітів, і (p - 1) / 2 теж просте
        [Fact]
        public void DhPrimeIs2048BitSafePrime()
        {
            BigInteger p = DiffieHellman.prime;

            Assert.Equal(2048, (int)p.GetBitLength());
            Assert.True(IsProbablePrime(p));
            Assert.True(IsProbablePrime((p - 1) / 2));
        }

        // Кейс D1 номер 20: спільний ключ однаковий на обох сторонах
        [Fact]
        public void DhSharedSecretIsSameOnBothSides()
        {
            BigInteger alicePrivate = DiffieHellman.GeneratePrivateKey();
            BigInteger bobPrivate = DiffieHellman.GeneratePrivateKey();
            BigInteger alicePublic = DiffieHellman.PublicKey(alicePrivate);
            BigInteger bobPublic = DiffieHellman.PublicKey(bobPrivate);

            Assert.Equal(
                DiffieHellman.SharedSecret(alicePrivate, bobPublic),
                DiffieHellman.SharedSecret(bobPrivate, alicePublic)
            );
            Assert.Equal(
                DiffieHellman.SharedPassword(alicePrivate, bobPublic),
                DiffieHellman.SharedPassword(bobPrivate, alicePublic)
            );
            Assert.Equal(64, DiffieHellman.SharedPassword(alicePrivate, bobPublic).Length);
        }

        // Небезпечні відкриті ключі 0, 1 і p - 1 відхиляються
        [Fact]
        public void DhRejectsBadPublicKeys()
        {
            BigInteger privateKey = DiffieHellman.GeneratePrivateKey();
            BigInteger[] badKeys = { 0, 1, DiffieHellman.prime - 1, DiffieHellman.prime };

            foreach (BigInteger key in badKeys)
            {
                bool rejected = false;
                try
                {
                    DiffieHellman.SharedSecret(privateKey, key);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }
                Assert.True(rejected);
            }
        }

        // Архів, запакований Алісою для Боба, відкриває Боб, а чужий ключ дає E_HMAC
        [Fact]
        public void DhArchiveOpensOnlyWithPartnerKey()
        {
            BigInteger alicePrivate = DiffieHellman.GeneratePrivateKey();
            BigInteger bobPrivate = DiffieHellman.GeneratePrivateKey();
            BigInteger evePrivate = DiffieHellman.GeneratePrivateKey();
            BigInteger alicePublic = DiffieHellman.PublicKey(alicePrivate);
            BigInteger bobPublic = DiffieHellman.PublicKey(bobPrivate);

            byte[] data = Encoding.UTF8.GetBytes("Архів для колеги без передавання пароля");
            string alicePassword = DiffieHellman.SharedPassword(alicePrivate, bobPublic);
            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("note.txt", data),
                alicePassword,
                new Settings()
            );

            string bobPassword = DiffieHellman.SharedPassword(bobPrivate, alicePublic);
            Assert.Equal(data, ArchiveReader.ReadArchive(volumes, bobPassword)[0].Data);

            string evePassword = DiffieHellman.SharedPassword(evePrivate, alicePublic);
            Assert.Equal(ErrorCodes.Hmac, TestHelpers.ErrorWhenReading(volumes, evePassword));
        }

        // Ключ записується у файл і читається назад без змін, пошкоджений файл дає зрозумілу помилку
        [Fact]
        public void DhKeySavesAndLoads()
        {
            string path = Path.GetTempFileName();
            try
            {
                BigInteger key = DiffieHellman.GeneratePrivateKey();
                DiffieHellman.SaveKey(path, key);
                Assert.Equal(key, DiffieHellman.LoadKey(path));

                File.WriteAllText(path, "не ключ");
                bool rejected = false;
                try
                {
                    DiffieHellman.LoadKey(path);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }
                Assert.True(rejected);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
