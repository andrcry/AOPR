using System.Numerics;
using System.Security.Cryptography;

namespace SimpleArchiver
{
    // Узгодження спільного ключа за Діффі - Геллманом (експертний рівень зони Захист).
    // Сценарій: передати архів колезі без передавання пароля.
    //
    // Кожен створює пару ключів: секретний a і відкритий A = g^a mod p. Відкриті ключі можна пересилати будь-як.
    // Спільний секрет: s = B^a mod p = A^b mod p = g^(a*b) mod p. Обидва отримують те саме число, не пересилаючи його.
    // Зі спільного секрету через SHA-256 виходить пароль архіву.
    //
    // Різниця з RSA-OAEP: у Діффі - Геллмана ключ ніхто не пересилає, обидві сторони обчислюють його самі.
    // У RSA-OAEP одна сторона вибирає випадковий сеансовий ключ, шифрує його відкритим ключем отримувача
    // і пересилає, а отримувач розшифровує його своїм секретним ключем
    public static class DiffieHellman
    {
        // Просте число p на 2048 бітів з RFC 3526 (група 14). Його використовують усі сторони
        const string primeHex =
            "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1"
            + "29024E088A67CC74020BBEA63B139B22514A08798E3404DD"
            + "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245"
            + "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED"
            + "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D"
            + "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F"
            + "83655D23DCA3AD961C62F356208552BB9ED529077096966D"
            + "670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B"
            + "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9"
            + "DE2BCBF6955817183995497CEA956AE515D2261898FA0510"
            + "15728E5A8AACAA68FFFFFFFFFFFFFFFF";

        const int privateKeyBytes = 32; // секретний ключ - випадкове число на 256 бітів

        public static readonly BigInteger prime = FromHex(primeHex); // модуль p
        public static readonly BigInteger generator = 2; // генератор g

        // Крок 1. Створює секретний ключ: випадкове число від 2 до p - 2
        public static BigInteger GeneratePrivateKey()
        {
            while (true)
            {
                byte[] bytes = RandomNumberGenerator.GetBytes(privateKeyBytes);
                var key = new BigInteger(bytes, true, true);

                if (key >= 2 && key <= prime - 2)
                    return key;
            }
        }

        // Крок 2. Відкритий ключ: A = g^a mod p
        public static BigInteger PublicKey(BigInteger privateKey) =>
            BigInteger.ModPow(generator, privateKey, prime);

        // Крок 3. Спільний секрет: s = B^a mod p, де B - відкритий ключ іншої сторони
        public static BigInteger SharedSecret(BigInteger privateKey, BigInteger otherPublicKey)
        {
            CheckPublicKey(otherPublicKey);
            return BigInteger.ModPow(otherPublicKey, privateKey, prime);
        }

        // Крок 4. Пароль архіву: SHA-256 від спільного секрету, записаний шістнадцятковими цифрами
        public static string SharedPassword(BigInteger privateKey, BigInteger otherPublicKey)
        {
            BigInteger secret = SharedSecret(privateKey, otherPublicKey);
            byte[] hash = SHA256.HashData(secret.ToByteArray(true, true));
            return Convert.ToHexString(hash);
        }

        // Відхиляє небезпечний відкритий ключ. Ключі 0, 1 і p - 1 дали б спільний секрет, який легко вгадати
        public static void CheckPublicKey(BigInteger key)
        {
            if (key < 2 || key > prime - 2)
                throw new ArgumentException(
                    "неправильний відкритий ключ: він має бути від 2 до p - 2"
                );
        }

        // Записує число шістнадцятковими цифрами
        public static string ToHex(BigInteger value) =>
            Convert.ToHexString(value.ToByteArray(true, true));

        // Читає число з шістнадцяткових цифр
        public static BigInteger FromHex(string hex) =>
            new BigInteger(Convert.FromHexString(hex.Trim()), true, true);

        // Зберігає ключ у текстовий файл
        public static void SaveKey(string path, BigInteger key)
        {
            File.WriteAllText(path, ToHex(key));
        }

        // Читає ключ з текстового файла
        public static BigInteger LoadKey(string path)
        {
            try
            {
                return FromHex(File.ReadAllText(path));
            }
            catch (FormatException)
            {
                throw new ArgumentException($"файл ключа пошкоджений: {path}");
            }
        }
    }
}
