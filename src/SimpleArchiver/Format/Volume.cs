namespace SimpleArchiver
{
    // Том = заголовок + шифротекст + HMAC. Спершу шифруємо, потім ставимо пломбу (Encrypt-then-MAC)
    public static class Volume
    {
        // Шифрує навантаження і ставить пломбу на заголовок і шифротекст
        public static byte[] Seal(
            int index,
            int count,
            byte[] salt,
            byte[] nonce,
            byte[] payload,
            Keys keys
        )
        {
            byte[] ciphertext = Crypto.AesCtr(keys.EncryptionKey, nonce, payload);

            var header = new VolumeHeader();
            header.VolumeIndex = index;
            header.VolumeCount = count;
            header.Salt = salt;
            header.Nonce = nonce;
            header.CiphertextLength = ciphertext.Length;

            var writer = new ByteWriter();
            writer.WriteBytes(header.ToBytes());
            writer.WriteBytes(ciphertext);
            writer.WriteBytes(Crypto.Hmac(keys.MacKey, writer.ToArray()));

            return writer.ToArray();
        }

        // Спершу перевіряє пломбу. Якщо не збіглася - відмова без розшифрування
        public static byte[] Open(byte[] volume, VolumeHeader header, Keys keys)
        {
            int signedLength = VolumeHeader.Size + header.CiphertextLength;

            var signedPart = new byte[signedLength];
            Array.Copy(volume, 0, signedPart, 0, signedLength);

            var storedTag = new byte[VolumeHeader.MacSize];
            Array.Copy(volume, signedLength, storedTag, 0, VolumeHeader.MacSize);

            if (!Crypto.SameBytes(Crypto.Hmac(keys.MacKey, signedPart), storedTag))
                throw new ArchiveException(
                    ErrorCodes.Hmac,
                    "пломба HMAC не збігається: архів змінено або пароль невірний"
                );

            var ciphertext = new byte[header.CiphertextLength];
            Array.Copy(volume, VolumeHeader.Size, ciphertext, 0, header.CiphertextLength);

            return Crypto.AesCtr(keys.EncryptionKey, header.Nonce, ciphertext);
        }
    }
}
