namespace SimpleArchiver
{
    // Коди відмови з розділу 5 специфікації
    public static class ErrorCodes
    {
        public const string Short = "E_SHORT"; // том коротший за 73 байти
        public const string Magic = "E_MAGIC"; // не SARC або не та версія
        public const string Length = "E_LENGTH"; // довжина тому не збігається з ciphertext_len
        public const string Hmac = "E_HMAC"; // пломба не збіглася або невірний пароль
        public const string Payload = "E_PAYLOAD"; // неправильні поля, імена чи довжини
        public const string Code = "E_CODE"; // неправильні коди Гаффмана або бітовий потік
        public const string Crc = "E_CRC"; // CRC32 файла не збігся
        public const string Volumes = "E_VOLUMES"; // неповний або змішаний набір томів
    }
}
