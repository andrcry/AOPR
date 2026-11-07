namespace SimpleArchiver
{
    // Таблиця кодів у файлі: nsym, далі пари (символ, довжина) за зростанням символу
    public static class CodeTable
    {
        // Записує таблицю довжин
        public static void Write(ByteWriter writer, int[] lengths)
        {
            int count = 0;
            foreach (int len in lengths)
            {
                if (len > 0)
                    count++;
            }

            writer.WriteU16(count);

            for (int s = 0; s < lengths.Length; s++)
            {
                if (lengths[s] == 0)
                    continue;

                writer.WriteU16(s);
                writer.WriteU8(lengths[s]);
            }
        }

        // Читає таблицю і перевіряє її. Розмір алфавіту: LIT = 257, LEN = 256, DIST = 16
        public static int[] Read(ByteReader reader, int alphabetSize)
        {
            int count = reader.ReadU16();
            if (count > alphabetSize)
                throw new ArchiveException(
                    ErrorCodes.Code,
                    "у таблиці більше символів, ніж в алфавіті"
                );

            var lengths = new int[alphabetSize];
            int previous = -1;

            for (int i = 0; i < count; i++)
            {
                int symbol = reader.ReadU16();
                int length = reader.ReadU8();

                if (symbol >= alphabetSize)
                    throw new ArchiveException(ErrorCodes.Code, $"символ {symbol} поза алфавітом");
                if (symbol <= previous)
                    throw new ArchiveException(
                        ErrorCodes.Code,
                        "символи таблиці не йдуть за зростанням"
                    );
                if (length < 1 || length > CanonicalCodes.MaxLength)
                    throw new ArchiveException(
                        ErrorCodes.Code,
                        $"довжина коду {length} поза межами від 1 до 15"
                    );

                lengths[symbol] = length;
                previous = symbol;
            }

            if (count == 1 && lengths[previous] != 1)
                throw new ArchiveException(ErrorCodes.Code, "єдиний символ повинен мати довжину 1");

            if (count > 1 && !CanonicalCodes.IsComplete(lengths))
                throw new ArchiveException(ErrorCodes.Code, "порушено рівність Крафта");

            return lengths;
        }
    }
}
