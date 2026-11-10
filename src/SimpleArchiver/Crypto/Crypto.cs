using System.Security.Cryptography;

namespace SimpleArchiver
{
    // Шифрування AES-CTR і пломба HMAC
    public static class Crypto
    {
        // AES-256-CTR. Шифрує блок лічильника nonce + номер (від 1) і робить XOR з даними.
        // Та сама функція і шифрує, і розшифровує
        public static byte[] AesCtr(byte[] key, byte[] nonce, byte[] input)
        {
            var output = new byte[input.Length];
            var counter = new byte[16];
            Array.Copy(nonce, 0, counter, 0, 12);

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                uint blockNumber = 1;

                for (int offset = 0; offset < input.Length; offset += 16)
                {
                    counter[12] = (byte)(blockNumber >> 24);
                    counter[13] = (byte)(blockNumber >> 16);
                    counter[14] = (byte)(blockNumber >> 8);
                    counter[15] = (byte)blockNumber;

                    byte[] keyStream = aes.EncryptEcb(counter, PaddingMode.None);

                    for (int j = 0; j < 16 && offset + j < input.Length; j++)
                        output[offset + j] = (byte)(input[offset + j] ^ keyStream[j]);

                    blockNumber++;
                }
            }

            return output;
        }

        // Пломба HMAC-SHA256 на 32 байти
        public static byte[] Hmac(byte[] key, byte[] data) => HMACSHA256.HashData(key, data);

        // Порівнює масиви завжди за однаковий час, щоб за часом не можна було вгадати пломбу
        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            int difference = 0;
            for (int i = 0; i < a.Length; i++)
                difference |= a[i] ^ b[i];

            return difference == 0;
        }
    }
}
