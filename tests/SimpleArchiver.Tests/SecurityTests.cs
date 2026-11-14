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

        // ---------- Модуль атаки (п. 3.7) ----------

        // Модель природного тексту, навчена на іншому тексті, ніж той, що атакуємо
        static ByteLanguageModel TextModel() =>
            ByteLanguageModel.FromSamples(new[] { SyntheticFiles.Text(20000, 99) });

        // ГА знаходить короткий ключ багатобайтового XOR і повністю відновлює текст
        [Fact]
        public void GaBreaksRepeatingKeyXor()
        {
            byte[] plain = SyntheticFiles.Text(3000, 5);
            byte[] key = Encoding.ASCII.GetBytes("K3y!x");
            byte[] cipher = WeakCipherAttack.Encrypt(plain, key, WeakCipherKind.Xor);
            Assert.NotEqual(plain, cipher);

            CipherAttackResult result = WeakCipherAttack.BreakWithGa(
                cipher,
                TextModel(),
                WeakCipherKind.Xor,
                new CipherGaOptions { Seed = 7 }
            );

            Assert.Equal(key.Length, result.EstimatedKeyLength);
            Assert.Equal(key, result.RecoveredKey);
            Assert.Equal(plain, result.Decrypted);
            Assert.Equal(1.0, WeakCipherAttack.Accuracy(plain, result.Decrypted));
        }

        // ГА знаходить ключ байтового шифру Віженера; найкраща пристосованість не спадає (елітизм)
        [Fact]
        public void GaBreaksVigenere()
        {
            byte[] plain = SyntheticFiles.Text(3000, 11);
            byte[] key = { 0x13, 0xA7, 0x5C, 0xF0, 0x2E, 0x81 };
            byte[] cipher = WeakCipherAttack.Encrypt(plain, key, WeakCipherKind.Vigenere);
            Assert.Equal(plain, WeakCipherAttack.Decrypt(cipher, key, WeakCipherKind.Vigenere));

            CipherAttackResult result = WeakCipherAttack.BreakWithGa(
                cipher,
                TextModel(),
                WeakCipherKind.Vigenere,
                new CipherGaOptions { Seed = 3 }
            );

            Assert.Equal(key, result.RecoveredKey);
            Assert.Equal(plain, result.Decrypted);
            for (int g = 1; g < result.BestByGeneration.Count; g++)
                Assert.True(result.BestByGeneration[g] >= result.BestByGeneration[g - 1]);
        }

        // Атака на метадані: без вирівнювання кожен файл впізнається за CiphertextLength,
        // з вирівнюванням відбитки збігаються і тип вгадати важче
        [Fact]
        public void PaddingNeutralizesMetadataAttack()
        {
            var files = new List<ArchiveFile>
            {
                new ArchiveFile("t1.txt", SyntheticFiles.Text(3000, 1)),
                new ArchiveFile("t2.txt", SyntheticFiles.Text(9000, 2)),
                new ArchiveFile("c1.csv", SyntheticFiles.Csv(5000, 3)),
                new ArchiveFile("c2.csv", SyntheticFiles.Csv(15000, 4)),
                new ArchiveFile("z1.z", SyntheticFiles.Compressed(2000, 5)),
                new ArchiveFile("z2.z", SyntheticFiles.Compressed(6000, 6)),
                new ArchiveFile("s1.dat", SyntheticFiles.Synthetic(4000, 7)),
                new ArchiveFile("s2.dat", SyntheticFiles.Synthetic(12000, 8)),
            };
            int[] types = { 0, 0, 2, 2, 5, 5, 6, 6 };

            MetadataLeakResult plain = WeakCipherAttack.AttackMetadata(
                files,
                types,
                new Settings(),
                "без вирівнювання",
                TestHelpers.Password
            );
            MetadataLeakResult padded = WeakCipherAttack.AttackMetadata(
                files,
                types,
                new Settings { PadUnit = 16384 },
                "кратно 16384",
                TestHelpers.Password
            );

            Assert.Equal(files.Count, plain.UniquelyIdentified);
            Assert.Equal(1.0, plain.TypeGuessAccuracy);
            Assert.True(padded.DistinctFingerprints < plain.DistinctFingerprints);
            Assert.True(padded.TypeGuessAccuracy < plain.TypeGuessAccuracy);
            Assert.True(padded.LeakBits < plain.LeakBits);
            Assert.True(padded.VisibleBytes > plain.VisibleBytes);
        }

        // Зміна будь-якого байта nonce, шифротексту чи пломби відбивається з кодом E_HMAC
        // (в однотомному архіві - і зміна солі); у багатотомному зміна солі дає E_VOLUMES ще до HMAC
        [Fact]
        public void TamperAttackIsRejectedWithHmac()
        {
            // Однотомний архів: будь-яка зміна байта - E_HMAC
            List<byte[]> single = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("a.txt", SyntheticFiles.Text(8000, 20)),
                TestHelpers.Password,
                new Settings()
            );
            Assert.Single(single);
            List<TamperResult> singleResults = WeakCipherAttack.TamperAttack(
                single,
                TestHelpers.Password,
                10,
                2001
            );
            Assert.All(singleResults, r => Assert.Equal(ErrorCodes.Hmac, r.Code));

            // Багатотомний архів
            var files = new List<ArchiveFile>
            {
                new ArchiveFile("a.txt", SyntheticFiles.Text(40000, 21)),
                new ArchiveFile("b.bin", SyntheticFiles.Compressed(50000, 22)),
            };
            var settings = new Settings { MaxVolumeBytes = 32768 };
            List<byte[]> volumes = ArchiveWriter.CreateArchive(files, TestHelpers.Password, settings);
            Assert.True(volumes.Count > 1);
            Assert.Equal("OK", WeakCipherAttack.ReadCode(volumes, TestHelpers.Password));

            List<TamperResult> results = WeakCipherAttack.TamperAttack(
                volumes,
                TestHelpers.Password,
                5,
                2001
            );

            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.True(r.Rejected));
            Assert.All(results, r => Assert.Equal(r.Expected, r.Code));
            Assert.All(
                results.Where(r => r.Region != "сіль"),
                r => Assert.Equal(ErrorCodes.Hmac, r.Code)
            );
        }
    }
}
