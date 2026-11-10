namespace SimpleArchiver
{
    // Перетворює біти назад у символи
    public class HuffmanDecoder
    {
        // Словник: за парою (довжина коду, код) знаходить символ
        Dictionary<(int Length, int Code), int> symbols =
            new Dictionary<(int Length, int Code), int>();

        // Готує словник кодів за довжинами з таблиці
        public HuffmanDecoder(int[] lengths)
        {
            int[] codes = CanonicalCodes.Make(lengths);

            for (int s = 0; s < lengths.Length; s++)
            {
                if (lengths[s] > 0)
                    symbols[(lengths[s], codes[s])] = s;
            }
        }

        // Читає біти по одному, поки не знайде символ з таким кодом. Код префіксний, тому збіг єдиний
        public int ReadSymbol(BitReader reader)
        {
            int code = 0;

            for (int len = 1; len <= CanonicalCodes.MaxLength; len++)
            {
                code = (code << 1) | reader.ReadBit();

                if (symbols.TryGetValue((len, code), out int symbol))
                    return symbol;
            }

            throw new ArchiveException(
                ErrorCodes.Code,
                "послідовність бітів не відповідає жодному коду"
            );
        }
    }
}
