namespace SimpleArchiver
{
    // Розархіватор. Порядок як у розділі 4.3: спершу перевіряє заголовки, потім пломбу HMAC, потім розшифровує
    // і розбирає томи, перевіряє повноту набору томів, декодує блоки, склеює файли і перевіряє CRC32
    public static class ArchiveReader
    {
        // Блоки одного файла, зібрані з усіх томів
        class FileAssembly
        {
            public FileRecord Record { get; } // опис файла з таблиці файлів
            public Dictionary<int, byte[]> Blocks { get; } = new Dictionary<int, byte[]>(); // номер блока і його байти

            // Створює порожній збірник для файла
            public FileAssembly(FileRecord record)
            {
                Record = record;
            }
        }

        // Перевіряє томи, розшифровує їх і повертає відновлені файли
        public static List<ArchiveFile> ReadArchive(List<byte[]> volumes, string password)
        {
            if (volumes.Count == 0)
                throw new ArchiveException(ErrorCodes.Volumes, "не передано жодного тому");

            var headers = new List<VolumeHeader>();
            foreach (byte[] volume in volumes)
                headers.Add(VolumeHeader.Parse(volume));

            // Томи одного архіву мають однакову сіль
            foreach (VolumeHeader header in headers)
            {
                if (!Crypto.SameBytes(header.Salt, headers[0].Salt))
                    throw new ArchiveException(ErrorCodes.Volumes, "томи належать різним архівам");
            }

            Keys keys = Keys.Derive(password, headers[0].Salt);

            var payloads = new List<Payload>();
            for (int i = 0; i < volumes.Count; i++)
                payloads.Add(Payload.Parse(Volume.Open(volumes[i], headers[i], keys)));

            CheckVolumeSet(headers);

            // Ставимо кожен том на його місце за номером, порядок подання не важливий
            var ordered = new Payload[headers.Count];
            for (int i = 0; i < headers.Count; i++)
                ordered[headers[i].VolumeIndex] = payloads[i];

            var assemblies = new List<FileAssembly>();
            var byName = new Dictionary<string, FileAssembly>();

            foreach (Payload payload in ordered)
            {
                foreach (FileRecord file in payload.Files)
                {
                    if (!byName.ContainsKey(file.Name))
                    {
                        var assembly = new FileAssembly(file);
                        byName[file.Name] = assembly;
                        assemblies.Add(assembly);
                        continue;
                    }

                    // Розмір і CRC стосуються цілого файла, тому в усіх томах однакові
                    FileRecord known = byName[file.Name].Record;
                    if (known.OriginalSize != file.OriginalSize || known.Crc != file.Crc)
                        throw new ArchiveException(
                            ErrorCodes.Payload,
                            $"томи по-різному описують файл {file.Name}"
                        );
                }

                // Блоки збираємо за іменем файла, бо file_index у кожному томі свій
                foreach (BlockRecord block in payload.Blocks)
                {
                    string name = payload.Files[block.FileIndex].Name;
                    FileAssembly assembly = byName[name];

                    if (assembly.Blocks.ContainsKey(block.BlockNumber))
                        throw new ArchiveException(
                            ErrorCodes.Payload,
                            $"блок {block.BlockNumber} файла {name} повторюється"
                        );

                    assembly.Blocks[block.BlockNumber] = DecodeBlock(block, payload.UsesPrediction);
                }
            }

            var result = new List<ArchiveFile>();
            foreach (FileAssembly assembly in assemblies)
                result.Add(JoinFile(assembly));

            return result;
        }

        // Розкодовує один блок. З прапорцем PREDICTION стиснутий блок починається із заголовка прогнозу
        static byte[] DecodeBlock(BlockRecord block, bool prediction)
        {
            if (block.IsNotCompressed)
                return block.Data;

            if (!prediction)
                return BlockCoder.Decode(block.Data, block.RawLength);

            var reader = new ByteReader(block.Data, ErrorCodes.Code);
            BytePredictor? predictor = BytePredictor.Read(reader);
            byte[] decoded = BlockCoder.Decode(reader.ReadBytes(reader.Remaining), block.RawLength);

            return predictor == null ? decoded : predictor.Restore(decoded);
        }

        // Перевіряє, що томів рівно volume_count, без повторів і з правильними номерами
        static void CheckVolumeSet(List<VolumeHeader> headers)
        {
            int count = headers[0].VolumeCount;

            foreach (VolumeHeader header in headers)
            {
                if (header.VolumeCount != count)
                    throw new ArchiveException(
                        ErrorCodes.Volumes,
                        "томи оголошують різну кількість томів"
                    );
            }

            if (headers.Count != count)
                throw new ArchiveException(
                    ErrorCodes.Volumes,
                    $"передано {headers.Count} томів, а має бути {count}"
                );

            var seen = new bool[count];
            foreach (VolumeHeader header in headers)
            {
                if (header.VolumeIndex >= count)
                    throw new ArchiveException(
                        ErrorCodes.Volumes,
                        "номер тому більший за кількість томів"
                    );
                if (seen[header.VolumeIndex])
                    throw new ArchiveException(
                        ErrorCodes.Volumes,
                        $"том {header.VolumeIndex} передано двічі"
                    );

                seen[header.VolumeIndex] = true;
            }
        }

        // Склеює блоки 0, 1, 2, ... і перевіряє розмір та CRC32
        static ArchiveFile JoinFile(FileAssembly assembly)
        {
            string name = assembly.Record.Name;
            int count = assembly.Blocks.Count;
            long total = 0;

            for (int n = 0; n < count; n++)
            {
                if (!assembly.Blocks.ContainsKey(n))
                    throw new ArchiveException(
                        ErrorCodes.Payload,
                        $"у файлі {name} пропущено блок {n}"
                    );

                total += assembly.Blocks[n].Length;
            }

            if (total != assembly.Record.OriginalSize)
                throw new ArchiveException(
                    ErrorCodes.Payload,
                    $"розмір файла {name} не збігається з orig_size"
                );

            var data = new byte[total];
            int position = 0;

            for (int n = 0; n < count; n++)
            {
                byte[] piece = assembly.Blocks[n];
                Array.Copy(piece, 0, data, position, piece.Length);
                position += piece.Length;
            }

            if (Crc32.Compute(data) != assembly.Record.Crc)
                throw new ArchiveException(ErrorCodes.Crc, $"CRC32 файла {name} не збігається");

            return new ArchiveFile(name, data);
        }
    }
}
