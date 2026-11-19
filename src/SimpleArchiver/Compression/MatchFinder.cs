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
                if (added + Lz77.MinMatch <= data.Length) // для хешу потрібні 3 байти
                {
                    int h = Hash(added);
                    previous[added] = head[h]; // нова позиція пам'ятає ту, що була останньою
                    head[h] = added; // і сама стає останньою
                }
                added++;
            }
        }

        // Спільний цикл пошуку для FindAll і FindLongest, щоб логіка була в одному місці
        // і обидва методи гарантовано давали узгоджені результати.
        // Повертає найдовший збіг (Length = 0, якщо збігу немає).
        // Якщо found не null, туди додається кожен збіг, довший за попередній.
        // Ланцюжок іде від ближчих позицій до дальших, тому серед однаково довгих
        // збігів перемагає найближчий (умова length > bestLength, а не >=)
        (int Length, int Distance) Search(int pos, List<(int Length, int Distance)>? found)
        {
            AddPositionsBefore(pos);

            if (pos + Lz77.MinMatch > data.Length) // до кінця менше 3 байтів
                return (0, 0);

            int maxLength = Math.Min(Lz77.MaxMatch, data.Length - pos); // не довше 258 і не за кінець
            int bestLength = Lz77.MinMatch - 1; // 2: рахуються збіги від 3 байтів
            int bestDistance = 0;
            int candidate = head[Hash(pos)]; // найближча позиція з тим самим хешем
            int checkedCount = 0;

            while (candidate >= 0 && checkedCount < maxChain)
            {
                int distance = pos - candidate;
                if (distance > Lz77.WindowSize) // далі 32 768 не можна, а далі ще дальші
                    break;

                int length = 0;
                while (length < maxLength && data[candidate + length] == data[pos + length])
                    length++; // побайтове порівняння

                if (length > bestLength)
                {
                    bestLength = length;
                    bestDistance = distance;
                    found?.Add((length, distance)); // для ДП зберігаємо кожне покращення
                    if (length == maxLength) // довшого не буде
                        break;
                }

                candidate = previous[candidate]; // наступний, дальший кандидат
                checkedCount++;
            }

            if (bestDistance == 0)
                return (0, 0); // жодного збігу довжиною хоча б MinMatch

            return (bestLength, bestDistance);
        }

        // Повертає всі корисні збіги для pos (множина M_i для ДП-розбору).
        // Ланцюжок іде від ближчих позицій до дальших, тому кожен наступний збіг довший
        // за попередній, але й відстань у нього більша.
        // Викликати з позиціями, які не зменшуються
        public List<(int Length, int Distance)> FindAll(int pos)
        {
            var found = new List<(int Length, int Distance)>();
            Search(pos, found);
            return found;
        }

        // Повертає найдовший збіг для pos. Length = 0, якщо збігу немає.
        // Не створює списку: жодних виділень пам'яті на кожну позицію.
        // Викликати з позиціями, які не зменшуються
        public (int Length, int Distance) FindLongest(int pos) => Search(pos, null);
    }
}
