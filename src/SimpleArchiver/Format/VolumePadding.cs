namespace SimpleArchiver
{
    // Вирівнювання розміру тому проти аналізу метаданих.
    // Без пароля сторонній бачить лише розмір тому, а за ним можна вгадати розмір і тип файлів.
    // Тому навантаження доповнюється нулями до кратного розміру або до степеня двійки.
    // Нулі лежать усередині шифротексту, тому ззовні їх не відрізнити від даних.
    // Формат SARC не має місця для доповнення, тому це наше командне розширення PADDING (прапорець 0x02)
    public static class VolumePadding
    {
        // Повертає довжину навантаження після вирівнювання.
        // length - довжина без доповнення, maxLength - найбільше навантаження, яке ще вміщується в том
        public static int PaddedLength(int length, Settings settings, int maxLength)
        {
            int target = length;

            // Крок 1. До найближчого степеня двійки: 1, 2, 4, 8, ..., 4096, 8192, ...
            if (settings.PadToPowerOfTwo)
            {
                target = 1;
                while (target < length)
                    target *= 2;
            }
            // Крок 2. Або до найближчого числа, кратного PadUnit
            else if (settings.PadUnit > 0)
            {
                target = (length + settings.PadUnit - 1) / settings.PadUnit * settings.PadUnit;
            }

            // Крок 3. Том не може стати більшим за дозволений розмір
            if (target > maxLength)
                target = maxLength;

            return Math.Max(target, length);
        }

        // true, якщо в налаштуваннях увімкнено будь-яке вирівнювання
        public static bool IsEnabled(Settings settings) =>
            settings.PadToPowerOfTwo || settings.PadUnit > 0;
    }
}
