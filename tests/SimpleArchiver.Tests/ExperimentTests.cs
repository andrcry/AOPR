namespace SimpleArchiver.Tests
{
    // Експеримент порядку операцій (crypto-order) і генератор звіту (report)
    public class ExperimentTests
    {
        // Шифротекст тієї самої довжини, що й дані, як у команді crypto-order
        static byte[] Encrypt(byte[] data)
        {
            Keys keys = Keys.Derive("test", new byte[16]);
            return Crypto.AesCtr(keys.EncryptionKey, new byte[12], data);
        }

        // Шифротекст AES-CTR має ентропію майже 8 біт на байт, а текст - помітно меншу
        [Fact]
        public void CiphertextEntropyIsAlmostEightBits()
        {
            byte[] text = SyntheticFiles.Text(20000, 1);
            var plain = TestHelpers.OneFile("a.txt", text);
            var encrypted = TestHelpers.OneFile("a.txt", Encrypt(text));

            Assert.True(CryptoOrderTable.Entropy(plain) < 6.0);
            Assert.True(CryptoOrderTable.Entropy(encrypted) > 7.9);
        }

        // Стиснути -> зашифрувати дає архів у кілька разів менший, ніж зашифрувати -> стиснути
        [Fact]
        public void CompressBeforeEncryptIsMuchSmaller()
        {
            byte[] text = SyntheticFiles.Text(20000, 2);
            var settings = TestHelpers.DefaultSettings();

            long compressFirst = CryptoOrderTable.ArchiveSize(TestHelpers.OneFile("a.txt", text), settings);
            long encryptFirst = CryptoOrderTable.ArchiveSize(TestHelpers.OneFile("a.txt", Encrypt(text)), settings);

            Assert.True(compressFirst * 2 < encryptFirst);
            // Шифротекст не стискається: архів не менший за самі дані
            Assert.True(encryptFirst >= text.Length);
        }
    }
}
