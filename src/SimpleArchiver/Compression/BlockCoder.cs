namespace SimpleArchiver
{
    // Кодування блока (розділ 3): три таблиці LIT, LEN, DIST, token_count, stream_len, потік бітів
    public static class BlockCoder
    {
        public const int LiteralAlphabet = 257; // 0-255 байти, 256 маркер посилання
        public const int LengthAlphabet = 256; // довжина збігу мінус 3
        public const int DistanceAlphabet = 16; // код відстані 0-15
        public const int MatchMarker = 256; // символ LIT, після якого йде посилання

        // Код відстані - скільки бітів займає число distance - 1. Наприклад, для відстані 12 це число 11,
        // у двійковому вигляді 1011, тобто 4 біти, і код дорівнює 4
        public static int DistanceCode(int distance)
        {
            int d = distance - 1;
            int bits = 0;

            while (d > 0)
            {
                bits++;
                d >>= 1;
            }

            return bits;
        }

        // Кодує список елементів у байти блока
        public static byte[] Encode(List<Token> tokens)
        {
            var literalCounts = new long[LiteralAlphabet];
            var lengthCounts = new long[LengthAlphabet];
            var distanceCounts = new long[DistanceAlphabet];

            // Рахуємо, скільки разів трапляється кожен символ
            foreach (Token token in tokens)
            {
                if (!token.IsMatch)
                {
                    literalCounts[token.Literal]++;
                    continue;
                }

                if (token.Length < Lz77.MinMatch || token.Length > Lz77.MaxMatch)
                    throw new ArgumentException($"неправильна довжина посилання {token.Length}");
                if (token.Distance < 1 || token.Distance > Lz77.WindowSize)
                    throw new ArgumentException($"неправильна відстань посилання {token.Distance}");

                literalCounts[MatchMarker]++;
                lengthCounts[token.Length - Lz77.MinMatch]++;
                distanceCounts[DistanceCode(token.Distance)]++;
            }

            // Будуємо три дерева Гаффмана і канонічні коди
            int[] literalLengths = HuffmanTree.BuildCodeLengths(literalCounts);
            int[] lengthLengths = HuffmanTree.BuildCodeLengths(lengthCounts);
            int[] distanceLengths = HuffmanTree.BuildCodeLengths(distanceCounts);

            int[] literalCodes = CanonicalCodes.Make(literalLengths);
            int[] lengthCodes = CanonicalCodes.Make(lengthLengths);
            int[] distanceCodes = CanonicalCodes.Make(distanceLengths);

            // Записуємо елементи бітами
            var bits = new BitWriter();

            foreach (Token token in tokens)
            {
                if (!token.IsMatch)
                {
                    bits.WriteBits(literalCodes[token.Literal], literalLengths[token.Literal]);
                    continue;
                }

                int lengthSymbol = token.Length - Lz77.MinMatch;
                int distanceCode = DistanceCode(token.Distance);
                int extraBits = distanceCode - 1;

                bits.WriteBits(literalCodes[MatchMarker], literalLengths[MatchMarker]);
                bits.WriteBits(lengthCodes[lengthSymbol], lengthLengths[lengthSymbol]);
                bits.WriteBits(distanceCodes[distanceCode], distanceLengths[distanceCode]);

                // Молодші extraBits бітів числа distance - 1 пишемо як є
                if (extraBits > 0)
                    bits.WriteBits((token.Distance - 1) & ((1 << extraBits) - 1), extraBits);
            }

            byte[] stream = bits.ToArray();

            var writer = new ByteWriter();
            CodeTable.Write(writer, literalLengths);
            CodeTable.Write(writer, lengthLengths);
            CodeTable.Write(writer, distanceLengths);
            writer.WriteU32((uint)tokens.Count);
            writer.WriteU32((uint)stream.Length);
            writer.WriteBytes(stream);

            return writer.ToArray();
        }

        // Розкодовує блок. rawLength - скільки байтів має вийти
        public static byte[] Decode(byte[] block, int rawLength)
        {
            var reader = new ByteReader(block, ErrorCodes.Code);

            int[] literalLengths = CodeTable.Read(reader, LiteralAlphabet);
            int[] lengthLengths = CodeTable.Read(reader, LengthAlphabet);
            int[] distanceLengths = CodeTable.Read(reader, DistanceAlphabet);

            uint tokenCount = reader.ReadU32();
            int streamLength = reader.ReadU32AsInt();

            if (streamLength > reader.Remaining)
                throw new ArchiveException(
                    ErrorCodes.Code,
                    "stream_len більший, ніж залишок блока"
                );
            if (streamLength < reader.Remaining)
                throw new ArchiveException(
                    ErrorCodes.Payload,
                    "після бітового потоку є зайві байти"
                );

            // raw_len прийшов з архіву, тому перед виділенням пам'яті перевіряємо, що він можливий.
            // Кожен елемент займає хоча б 1 біт і дає не більше MaxMatch байтів, тому з потоку
            // довжиною stream_len байтів не може вийти більше ніж min(token_count, stream_len * 8) * MaxMatch.
            // Без цієї перевірки архів на кілька байтів міг би змусити виділити ~2 ГБ
            long maxTokens = Math.Min(tokenCount, (long)streamLength * 8);
            if (rawLength < 0 || rawLength > maxTokens * Lz77.MaxMatch)
                throw new ArchiveException(
                    ErrorCodes.Payload,
                    "raw_len більший, ніж можуть дати елементи блока"
                );

            var bits = new BitReader(reader.ReadBytes(streamLength));
            var literalDecoder = new HuffmanDecoder(literalLengths);
            var lengthDecoder = new HuffmanDecoder(lengthLengths);
            var distanceDecoder = new HuffmanDecoder(distanceLengths);

            // Розмір результату відомий наперед: один масив, жодних розширень і фінального копіювання
            var output = new byte[rawLength];
            int written = 0; // скільки байтів уже розкодовано

            for (uint i = 0; i < tokenCount; i++)
            {
                int symbol = literalDecoder.ReadSymbol(bits);

                if (symbol < MatchMarker)
                {
                    if (written >= rawLength)
                        throw new ArchiveException(ErrorCodes.Payload, "даних більше, ніж raw_len");

                    output[written++] = (byte)symbol;
                    continue;
                }

                int length = lengthDecoder.ReadSymbol(bits) + Lz77.MinMatch;
                int code = distanceDecoder.ReadSymbol(bits);
                int distance =
                    code <= 1 ? code + 1 : ((1 << (code - 1)) | bits.ReadBits(code - 1)) + 1;

                if (distance > written)
                    throw new ArchiveException(
                        ErrorCodes.Code,
                        "посилання вказує за початок даних"
                    );
                if (written + length > rawLength)
                    throw new ArchiveException(ErrorCodes.Payload, "даних більше, ніж raw_len");

                // Копіюємо по одному байту, тому збіг може накладатися сам на себе
                // (наприклад, відстань 1 і довжина 10 дають 10 однакових байтів)
                int start = written - distance;
                for (int k = 0; k < length; k++)
                    output[written++] = output[start + k];
            }

            bits.CheckEndIsClean();

            if (written != rawLength)
                throw new ArchiveException(
                    ErrorCodes.Payload,
                    "розмір розкодованого блока не дорівнює raw_len"
                );

            return output; // масив уже рівно потрібного розміру, ToArray не потрібен
        }
    }
}
