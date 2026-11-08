namespace SimpleArchiver
{
    // Спосіб розбору LZ77. Це число записується в поле strategy таблиці файлів
    // (0 - файл збережено без стиснення)
    public enum ParserKind
    {
        Greedy = 1, // жадібний
        Lazy = 2, // лінива жадібність
    }
}
