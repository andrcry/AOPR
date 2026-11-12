namespace SimpleArchiver
{
    // Розкладає блоки по томах (розділ 7). Для кожного тому: фіксує таблицю файлів,
    // рахує місткість, рюкзаком обирає блоки, решту лишає на наступний том
    public static class VolumePacker
    {
        // 41 заголовок + 32 HMAC + 6 байтів file_count, flags, reserved, block_count
        public const int FixedOverhead = VolumeHeader.Size + VolumeHeader.MacSize + 6;
        const int maxCount = 65535; // найбільше значення поля uint16

        // Розкладає всі блоки по томах і повертає навантаження кожного тому
        public static List<Payload> Pack(
            List<FileRecord> files,
            List<PreparedBlock> blocks,
            Settings settings
        )
        {
            var volumes = new List<Payload>();
            var remaining = new List<PreparedBlock>(blocks);
            bool isFirst = true;

            // Хоча б один том є завжди, навіть для порожнього архіву
            while (isFirst || remaining.Count > 0)
            {
                List<int> fileNumbers = FilesForTable(files, remaining);
                if (fileNumbers.Count > maxCount)
                    throw new InvalidOperationException("забагато файлів для одного тому");

                int tableSize = 0;
                foreach (int f in fileNumbers)
                    tableSize += files[f].SizeInTable();

                int capacity = settings.MaxVolumeBytes - FixedOverhead - tableSize;
                if (capacity < 0)
                    throw new InvalidOperationException(
                        "таблиця файлів не вміщується в том: збільште MaxVolumeBytes"
                    );

                var weights = new int[remaining.Count];
                var values = new int[remaining.Count];

                for (int i = 0; i < remaining.Count; i++)
                {
                    weights[i] = remaining[i].Weight();
                    values[i] = settings.GetPriority(files[remaining[i].FileNumber].FileType);

                    if (weights[i] > capacity)
                        throw new InvalidOperationException(
                            "блок не вміщується навіть у порожній том: зменшіть BlockSize або збільште MaxVolumeBytes"
                        );
                }

                List<int> chosen = ChooseBlocks(weights, values, capacity, settings.UseDpKnapsack);
                if (chosen.Count > maxCount)
                    throw new InvalidOperationException("забагато блоків для одного тому");

                volumes.Add(MakePayload(files, fileNumbers, remaining, chosen, settings));
                remaining = RemoveChosen(remaining, chosen);
                isFirst = false;

                if (volumes.Count > maxCount)
                    throw new InvalidOperationException("забагато томів");
            }

            return volumes;
        }

        // Якщо все вміщується, бере все. Інакше розв'язує рюкзак
        public static List<int> ChooseBlocks(int[] weights, int[] values, int capacity, bool useDp)
        {
            long total = 0;
            foreach (int w in weights)
                total += w;

            if (total <= capacity)
            {
                var all = new List<int>();
                for (int i = 0; i < weights.Length; i++)
                    all.Add(i);
                return all;
            }

            return useDp
                ? Knapsack.SolveDp(weights, values, capacity)
                : Knapsack.SolveGreedy(weights, values, capacity);
        }

        // Файли, у яких ще є блоки. Порожній файл теж має один блок (raw_len = 0), тому окремого правила не треба
        static List<int> FilesForTable(List<FileRecord> files, List<PreparedBlock> remaining)
        {
            var hasBlocks = new bool[files.Count];
            foreach (PreparedBlock block in remaining)
                hasBlocks[block.FileNumber] = true;

            var result = new List<int>();
            for (int f = 0; f < files.Count; f++)
            {
                if (hasBlocks[f])
                    result.Add(f);
            }

            return result;
        }

        // Складає навантаження тому. Номер файла в архіві перетворює на номер у таблиці цього тому
        static Payload MakePayload(
            List<FileRecord> files,
            List<int> fileNumbers,
            List<PreparedBlock> remaining,
            List<int> chosen,
            Settings settings
        )
        {
            var payload = new Payload();
            payload.Flags = settings.UsePrediction ? Payload.PredictionFlag : 0;
            var localIndex = new Dictionary<int, int>();

            foreach (int f in fileNumbers)
            {
                localIndex[f] = payload.Files.Count;
                payload.Files.Add(files[f]);
            }

            chosen.Sort();

            foreach (int i in chosen)
            {
                PreparedBlock prepared = remaining[i];
                var record = new BlockRecord();
                record.FileIndex = localIndex[prepared.FileNumber];
                record.BlockNumber = prepared.BlockNumber;
                record.RawLength = prepared.RawLength;
                record.Flags = prepared.Flags;
                record.Data = prepared.Data;
                payload.Blocks.Add(record);
            }

            return payload;
        }

        // Повертає блоки, які не обрали
        static List<PreparedBlock> RemoveChosen(List<PreparedBlock> remaining, List<int> chosen)
        {
            var isChosen = new bool[remaining.Count];
            foreach (int i in chosen)
                isChosen[i] = true;

            var left = new List<PreparedBlock>();
            for (int i = 0; i < remaining.Count; i++)
            {
                if (!isChosen[i])
                    left.Add(remaining[i]);
            }

            return left;
        }
    }
}
