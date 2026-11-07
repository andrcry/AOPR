namespace SimpleArchiver
{
    // Вузол дерева Гаффмана. Листок - символ, внутрішній вузол - батько двох вузлів
    public class HuffmanNode
    {
        public int Symbol { get; init; } = -1; // номер символу, -1 у внутрішнього вузла
        public long Weight { get; init; } // частота символу або сума частот дітей
        public HuffmanNode? Left { get; init; } // ліва дитина, дуга 0
        public HuffmanNode? Right { get; init; } // права дитина, дуга 1

        public bool IsLeaf => Left == null && Right == null; // true, якщо у вузла немає дітей
    }
}
