namespace SimpleArchiver
{
    // Будує дерево Гаффмана і повертає довжину коду для кожного символу.
    // Самі коди потім рахує CanonicalCodes (так вимагає специфікація)
    public static class HuffmanTree
    {
        public const int MaxCodeLength = 15; // найбільша дозволена довжина коду

        // Будує довжини кодів. Якщо код вийшов довшим за 15 бітів, ділить частоти навпіл і будує знову
        public static int[] BuildCodeLengths(long[] frequencies)
        {
            long[] freq = (long[])frequencies.Clone();

            while (true)
            {
                int[] lengths = BuildOnce(freq);
                if (MaxOf(lengths) <= MaxCodeLength)
                    return lengths;

                // Згладжування частот: різниця між частими і рідкісними символами зменшується
                for (int s = 0; s < freq.Length; s++)
                {
                    if (freq[s] > 1)
                        freq[s] /= 2;
                }
            }
        }

        // Будує дерево один раз і повертає глибини листків
        static int[] BuildOnce(long[] freq)
        {
            var lengths = new int[freq.Length];

            // Черга з пріоритетом
            // Другий ключ - порядок вставки, щоб при однакових частотах результат був завжди однаковий
            var queue = new PriorityQueue<HuffmanNode, (long Weight, int Order)>();
            int order = 0;

            for (int s = 0; s < freq.Length; s++)
            {
                if (freq[s] > 0)
                    queue.Enqueue(
                        new HuffmanNode { Symbol = s, Weight = freq[s] },
                        (freq[s], order++)
                    );
            }

            if (queue.Count == 0)
                return lengths;

            // Особливий випадок: єдиний символ має довжину 1 (код 0)
            if (queue.Count == 1)
            {
                lengths[queue.Dequeue().Symbol] = 1;
                return lengths;
            }

            // Злиття двох найменших вузлів, доки не залишиться один
            while (queue.Count > 1)
            {
                HuffmanNode a = queue.Dequeue();
                HuffmanNode b = queue.Dequeue();
                var parent = new HuffmanNode
                {
                    Weight = a.Weight + b.Weight,
                    Left = a,
                    Right = b,
                };
                queue.Enqueue(parent, (parent.Weight, order++));
            }

            // Довжина коду = глибина листка
            SaveDepths(queue.Dequeue(), 0, lengths);
            return lengths;
        }

        // Записує глибину кожного листка в масив довжин
        static void SaveDepths(HuffmanNode node, int depth, int[] lengths)
        {
            if (node.IsLeaf)
            {
                lengths[node.Symbol] = depth;
                return;
            }

            SaveDepths(node.Left!, depth + 1, lengths);
            SaveDepths(node.Right!, depth + 1, lengths);
        }

        // Повертає найбільше число в масиві
        static int MaxOf(int[] values)
        {
            int max = 0;
            foreach (int value in values)
            {
                if (value > max)
                    max = value;
            }
            return max;
        }
    }
}
