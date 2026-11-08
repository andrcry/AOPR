namespace SimpleArchiver
{
    // Шукає повтори для позиції. Щоб не перебирати всі позиції, тримає ланцюжки
    // позицій з однаковим хешем перших трьох байтів
    public class MatchFinder
    {
        const int hashSize = 65536; // кількість кошиків для хешу

        byte[] data; // дані, в яких шукаємо повтори
        int[] head; // head[хеш] - остання позиція з таким хешем або -1
        int[] previous; // previous[позиція] - попередня позиція з тим самим хешем або -1
        int added; // скільки позицій уже додано в ланцюжки
        int maxChain; // скільки кандидатів перевіряти найбільше

        // Створює пошук збігів для масиву даних
        public MatchFinder(byte[] data, int maxChain)
        {
            this.data = data;
            this.maxChain = maxChain;

            head = new int[hashSize];
            for (int i = 0; i < hashSize; i++)
                head[i] = -1;

            previous = new int[data.Length];
        }

        // Хеш трьох байтів. Однакові три байти дають однаковий хеш
        int Hash(int pos) =>
            ((data[pos] << 8) ^ (data[pos + 1] << 4) ^ data[pos + 2]) & (hashSize - 1);

        // Додає в ланцюжки всі позиції до pos
        void AddPositionsBefore(int pos)
        {
            while (added < pos)
            {
                if (added + Lz77.MinMatch <= data.Length)
                {
                    int h = Hash(added);
                    previous[added] = head[h];
                    head[h] = added;
                }
                added++;
            }
        }

        // Повертає всі корисні збіги для pos (множина M_i для ДП-розбору).
        // Ланцюжок іде від ближчих позицій до дальших, тому кожен наступний збіг довший
        // за попередній, але й відстань у нього більша.
        // Викликати з позиціями, які не зменшуються
        public List<(int Length, int Distance)> FindAll(int pos)
        {
            AddPositionsBefore(pos);
            var found = new List<(int Length, int Distance)>();

            if (pos + Lz77.MinMatch > data.Length)
                return found;

            int maxLength = Math.Min(Lz77.MaxMatch, data.Length - pos);
            int bestLength = Lz77.MinMatch - 1;
            int candidate = head[Hash(pos)];
            int checkedCount = 0;

            while (candidate >= 0 && checkedCount < maxChain)
            {
                int distance = pos - candidate;
                if (distance > Lz77.WindowSize)
                    break;

                int length = 0;
                while (length < maxLength && data[candidate + length] == data[pos + length])
                    length++;

                if (length > bestLength)
                {
                    bestLength = length;
                    found.Add((length, distance));
                    if (length == maxLength)
                        break;
                }

                candidate = previous[candidate];
                checkedCount++;
            }

            return found;
        }

        // Повертає найдовший збіг для pos. Length = 0, якщо збігу немає
        public (int Length, int Distance) FindLongest(int pos)
        {
            List<(int Length, int Distance)> found = FindAll(pos);

            if (found.Count == 0)
                return (0, 0);

            return found[found.Count - 1];
        }
    }
}
