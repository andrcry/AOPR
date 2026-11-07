namespace SimpleArchiver
{
    // Елемент блока за специфікацією: або літерал (один байт), або посилання "назад на Distance, взяти Length байтів"
    public class Token
    {
        public bool IsMatch { get; set; } // true - посилання, false - літерал
        public byte Literal { get; set; } // байт літерала
        public int Length { get; set; } // довжина посилання, від 3 до 258
        public int Distance { get; set; } // відстань назад, від 1 до 32768

        // Створює літерал
        public static Token MakeLiteral(byte value)
        {
            var token = new Token();
            token.Literal = value;
            return token;
        }

        // Створює посилання
        public static Token MakeMatch(int length, int distance)
        {
            var token = new Token();
            token.IsMatch = true;
            token.Length = length;
            token.Distance = distance;
            return token;
        }
    }
}
