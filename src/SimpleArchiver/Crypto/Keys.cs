using System.Security.Cryptography;
using System.Text;

namespace SimpleArchiver
{
    // Два різні ключі з пароля: для AES і для HMAC
    public class Keys
    {
        const int iterations = 100000; // кількість повторів PBKDF2

        public byte[] EncryptionKey { get; } // ключ шифрування K_enc
        public byte[] MacKey { get; } // ключ пломби K_mac

        // Зберігає два готові ключі
        Keys(byte[] encryptionKey, byte[] macKey)
        {
            EncryptionKey = encryptionKey;
            MacKey = macKey;
        }

        // PBKDF2-HMAC-SHA256, 64 байти. Перші 32 - K_enc, останні 32 - K_mac. Викликати один раз на архів
        public static Keys Derive(string password, byte[] salt)
        {
            byte[] master = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                64
            );

            var encryptionKey = new byte[32];
            var macKey = new byte[32];
            Array.Copy(master, 0, encryptionKey, 0, 32);
            Array.Copy(master, 32, macKey, 0, 32);

            return new Keys(encryptionKey, macKey);
        }
    }
}
