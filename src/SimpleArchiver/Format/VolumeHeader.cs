namespace SimpleArchiver
{
    // Відкритий заголовок тому, 41 байт: SARC, версія, номер тому, кількість томів, сіль, nonce, ciphertext_len
    public class VolumeHeader
    {
        public const int Size = 41; // розмір заголовка, байтів
        public const int MacSize = 32; // розмір пломби HMAC, байтів
        public const int MinVolumeSize = Size + MacSize; // 73

        const string magic = "SARC"; // перші чотири байти тому
        const int version = 1; // версія формату

        public int VolumeIndex { get; set; } // номер тому від 0
        public int VolumeCount { get; set; } // кількість томів в архіві
        public byte[] Salt { get; set; } = new byte[16]; // сіль для виведення ключів
        public byte[] Nonce { get; set; } = new byte[12]; // nonce для AES-CTR
        public int CiphertextLength { get; set; } // довжина шифротексту

        // Записує заголовок у 41 байт
        public byte[] ToBytes()
        {
            var writer = new ByteWriter();

            foreach (char letter in magic)
                writer.WriteU8(letter);

            writer.WriteU8(version);
            writer.WriteU16(VolumeIndex);
            writer.WriteU16(VolumeCount);
            writer.WriteBytes(Salt);
            writer.WriteBytes(Nonce);
            writer.WriteU32((uint)CiphertextLength);

            return writer.ToArray();
        }

        // Перевіряє довжину, magic і версію. Нічого не розшифровує
        public static VolumeHeader Parse(byte[] volume)
        {
            if (volume.Length < MinVolumeSize)
                throw new ArchiveException(
                    ErrorCodes.Short,
                    $"том коротший за {MinVolumeSize} байти"
                );

            for (int i = 0; i < magic.Length; i++)
            {
                if (volume[i] != magic[i])
                    throw new ArchiveException(ErrorCodes.Magic, "це не архів SARC");
            }

            if (volume[4] != version)
                throw new ArchiveException(ErrorCodes.Magic, $"невідома версія {volume[4]}");

            var reader = new ByteReader(volume, ErrorCodes.Short);
            reader.ReadBytes(5);

            var header = new VolumeHeader();
            header.VolumeIndex = reader.ReadU16();
            header.VolumeCount = reader.ReadU16();
            header.Salt = reader.ReadBytes(16);
            header.Nonce = reader.ReadBytes(12);
            uint cipherLength = reader.ReadU32();

            if ((long)Size + cipherLength + MacSize != volume.Length)
                throw new ArchiveException(
                    ErrorCodes.Length,
                    "довжина тому не відповідає ciphertext_len"
                );

            header.CiphertextLength = (int)cipherLength;
            return header;
        }
    }
}
