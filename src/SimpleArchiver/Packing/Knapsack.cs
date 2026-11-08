using System.Collections;

namespace SimpleArchiver
{
    // Рюкзак 0/1: обрати предмети з найбільшою цінністю, щоб вага не перевищила місткість.
    // Предмет - блок, вага - його розмір, рюкзак - том
    public static class Knapsack
    {
        // Динамічне програмування, O(n * W).
        // T[i][c] = max(T[i-1][c], T[i-1][c - w_i] + v_i).
        // Тримаємо лише два рядки таблиці і біт "предмет взяли" для зворотного ходу
        public static List<int> SolveDp(int[] weights, int[] values, int capacity)
        {
            int n = weights.Length;
            var previousRow = new long[capacity + 1];
            var currentRow = new long[capacity + 1];
            var took = new BitArray[n];

            for (int i = 0; i < n; i++)
            {
                took[i] = new BitArray(capacity + 1);

                for (int c = 0; c <= capacity; c++)
                {
                    currentRow[c] = previousRow[c];

                    if (weights[i] <= c && previousRow[c - weights[i]] + values[i] > currentRow[c])
                    {
                        currentRow[c] = previousRow[c - weights[i]] + values[i];
                        took[i][c] = true;
                    }
                }

                long[] temp = previousRow;
                previousRow = currentRow;
                currentRow = temp;
            }

            // Зворотний хід від останнього предмета до першого
            var chosen = new List<int>();
            int place = capacity;

            for (int i = n - 1; i >= 0; i--)
            {
                if (!took[i][place])
                    continue;

                chosen.Add(i);
                place -= weights[i];
            }

            chosen.Reverse();
            return chosen;
        }

        // Жадібний: сортує за цінністю на одиницю ваги і бере все, що вміщується
        public static List<int> SolveGreedy(int[] weights, int[] values, int capacity)
        {
            int n = weights.Length;
            var ratios = new double[n];
            var order = new int[n];

            for (int i = 0; i < n; i++)
            {
                ratios[i] = (double)values[i] / weights[i];
                order[i] = i;
            }

            // Сортує order за ratios від меншого до більшого
            Array.Sort(ratios, order);

            var chosen = new List<int>();
            long used = 0;

            for (int k = n - 1; k >= 0; k--)
            {
                int item = order[k];
                if (used + weights[item] > capacity)
                    continue;

                chosen.Add(item);
                used += weights[item];
            }

            chosen.Sort();
            return chosen;
        }

        // Сумарна цінність обраних предметів
        public static long TotalValue(List<int> chosen, int[] values)
        {
            long sum = 0;
            foreach (int item in chosen)
                sum += values[item];
            return sum;
        }

        // Сумарна вага обраних предметів
        public static long TotalWeight(List<int> chosen, int[] weights)
        {
            long sum = 0;
            foreach (int item in chosen)
                sum += weights[item];
            return sum;
        }
    }
}
