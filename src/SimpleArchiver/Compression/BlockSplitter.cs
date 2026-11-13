namespace SimpleArchiver
{
    // Розбиває файл на блоки. Кожен блок має власні таблиці Гаффмана
    public static class BlockSplitter
    {
        // Звичайне розбиття: блоки по BlockSize байтів
        public static List<PreparedBlock> SplitFixed(byte[] data, Settings settings, bool compress)
        {
            if (settings.BlockSize <= 0)
                throw new ArgumentException("BlockSize повинен бути додатним");

            if (data.Length == 0)
                return EmptyFile();

            var blocks = new List<PreparedBlock>();

            for (int offset = 0; offset < data.Length; offset += settings.BlockSize)
            {
                int size = Math.Min(settings.BlockSize, data.Length - offset);
                blocks.Add(
                    ArchiveWriter.EncodeBlock(Slice(data, offset, size), settings, compress)
                );
            }

            return blocks;
        }

        // Порожній файл: один блок raw_len = 0 без стиснення, так само як в еталонному архіві ref_02_empty_file.
        // Тоді кожен файл має хоча б один блок, і рюкзак кладе порожній файл у том, як і всі інші
        static List<PreparedBlock> EmptyFile()
        {
            var block = new PreparedBlock();
            block.RawLength = 0;
            block.Data = new byte[0];
            block.Flags = BlockRecord.NoCompressionFlag;

            var blocks = new List<PreparedBlock>();
            blocks.Add(block);
            return blocks;
        }

        // ДП-розбиття на блоки з власними таблицями (просунутий рівень зони Стиснення).
        // Межі блоків шукаємо з кроком SplitUnit байтів: дані ділимо на шматки, блок - це кілька шматків поспіль.
        //   F[0] = 0
        //   F[j] = min по i < j ( F[i] + T(i,j) + H(i,j) )
        // де T(i,j) - розмір таблиць Гаффмана блока з шматків i-j-1, H(i,j) - розмір його даних.
        // T + H - це рівно coded_len блока, плюс 15 байтів запису в таблиці блоків.
        // Блок не довший за BlockSize, тому i беремо лише з останніх BlockSize / SplitUnit шматків
        public static List<PreparedBlock> SplitDp(byte[] data, Settings settings)
        {
            // BlockSize = 0 теж ділиться на unit без остачі, але тоді maxSpan = 0, жоден стан ДП не досяжний,
            // а зворотний хід склеїв би весь файл в один блок понад ліміт. Тому BlockSize має бути додатним
            int unit = settings.SplitUnit;
            if (unit <= 0 || settings.BlockSize <= 0 || settings.BlockSize % unit != 0)
                throw new ArgumentException("BlockSize повинен бути додатним кратним SplitUnit");

            if (data.Length == 0)
                return EmptyFile();

            int pieces = (data.Length + unit - 1) / unit;
            int maxSpan = settings.BlockSize / unit;

            // Крок 1. F[0] = 0, решта - нескінченність
            var best = new long[pieces + 1];
            var from = new int[pieces + 1];
            for (int j = 1; j <= pieces; j++)
                best[j] = long.MaxValue;

            // Крок 2. Для кожного кінця j перебрати всі можливі початки i
            for (int j = 1; j <= pieces; j++)
            {
                for (int i = Math.Max(0, j - maxSpan); i < j; i++)
                {
                    long cost = best[i] + BlockCost(data, i, j, settings);

                    if (cost < best[j])
                    {
                        best[j] = cost;
                        from[j] = i;
                    }
                }
            }

            // Крок 3. Зворотний хід: від кінця до початку збираємо межі блоків
            var starts = new List<int>();
            for (int j = pieces; j > 0; j = from[j])
                starts.Add(from[j]);
            starts.Reverse();

            // Крок 4. Закодувати обрані блоки
            var blocks = new List<PreparedBlock>();
            for (int k = 0; k < starts.Count; k++)
            {
                int end = k + 1 < starts.Count ? starts[k + 1] : pieces;
                int offset = starts[k] * unit;
                int size = Math.Min(end * unit, data.Length) - offset;
                blocks.Add(ArchiveWriter.EncodeBlock(Slice(data, offset, size), settings, true));
            }

            return blocks;
        }

        // T(i,j) + H(i,j) + 15: скільки байтів займе блок із шматків i-j-1
        static long BlockCost(byte[] data, int i, int j, Settings settings)
        {
            int offset = i * settings.SplitUnit;
            int size = Math.Min(j * settings.SplitUnit, data.Length) - offset;
            return ArchiveWriter.EncodeBlock(Slice(data, offset, size), settings, true).Weight();
        }

        // Сумарний розмір блоків разом із записами таблиці блоків
        public static long TotalSize(List<PreparedBlock> blocks)
        {
            long sum = 0;
            foreach (PreparedBlock block in blocks)
                sum += block.Weight();
            return sum;
        }

        // Вирізає з даних шматок довжиною size, починаючи з offset
        static byte[] Slice(byte[] data, int offset, int size)
        {
            var part = new byte[size];
            Array.Copy(data, offset, part, 0, size);
            return part;
        }
    }
}
