namespace SimpleArchiver
{
    // Помилка архіву. Code - код зі специфікації, наприклад E_HMAC
    public class ArchiveException : Exception
    {
        public string Code { get; } // код помилки зі специфікації

        // Створює помилку з кодом і поясненням
        public ArchiveException(string code, string message)
            : base($"{code}: {message}")
        {
            Code = code;
        }
    }
}
