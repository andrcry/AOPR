namespace SimpleArchiver
{
    // Будує дерево Гаффмана і повертає довжину коду для кожного символу.
    // Самі коди потім рахує CanonicalCodes (так вимагає специфікація)
    public static class HuffmanTree
    {
        public const int MaxCodeLength = 15; // найбільша дозволена довжина коду

        // Будує довжини кодів. Якщо код вийшов довшим за 15 бітів, ділить частоти навпіл і будує знову.
        // Цикл завжди закінчується: після кількох ділень навпіл кожна ненульова частота стає 1,
        // а коли всі частоти однакові, дерево збалансоване і коди нашого алфавіту не довші за 15 бітів
        public static int[] BuildCodeLengths(long[] frequencies)
        {
            long[] freq = (long[])frequencies.Clone();
            bool isWorking = true;  // true, поки ще пробуємо побудувати коди

            do
            {
                int[] lengths = BuildOnce(freq);
                if (MaxOf(lengths) <= MaxCodeLength)
                {
                    // Коди вклалися в 15 бітів: більше пробувати не треба
                    isWorking = false;
                    return lengths;
                }

                // Згладжування частот: різниця між частими і рідкісними символами зменшується
                for (int s = 0; s < freq.Length; s++)
                {
                    if (freq[s] > 1)
                        freq[s] /= 2;
                }
            }
            while (isWorking);

            throw new InvalidOperationException($"не вдалося побудувати коди Гаффмана не довші за {MaxCodeLength} бітів");
        }

        // Будує дерево один раз і повертає глибини листків
        static int[] BuildOnce(long[] freq)
        {
            var lengths = new int[freq.Length];

            // Черга з пріоритетом: першим виходить вузол з найменшою частотою. Order розрізняє вузли з однаковою частотою, щоб дерево завжди будувалося однаково
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

            // Жадібний вибір: беремо два найрідші вузли і зливаємо їх в один, доки не залишиться один корінь
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

            // Довжина коду символу дорівнює глибині його листка в дереві
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
