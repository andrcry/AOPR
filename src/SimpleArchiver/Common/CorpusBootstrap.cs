using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SimpleArchiver
{
    // Генерує відсутні файли корпусу, train/valid та team_config.json для перевірки всіх чекпоінтів
    public static class CorpusBootstrap
    {
        public static void EnsureDataExists(string baseDir = "data")
        {
            Directory.CreateDirectory(baseDir);
            EnsureTeamConfig(Path.Combine(baseDir, "team_config.json"));
            EnsureCorpus(Path.Combine(baseDir, "corpus"));
            EnsureDataset(Path.Combine(baseDir, "train"), countPerGroup: 10, seed: 100);
            EnsureDataset(Path.Combine(baseDir, "valid"), countPerGroup: 5, seed: 200);
        }

        static void EnsureTeamConfig(string path)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 10)
                return;

            string json = """
            {
              "alpha": 0.7,
              "beta": 0.3,
              "t_ref": 0.5,
              "max_volume_bytes": 1048576,
              "block_size": 65536,
              "F1_min": 0.75,
              "пріоритети_типів": [1, 1, 1, 1, 1, 1, 1]
            }
            """;
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        static void EnsureCorpus(string corpusDir)
        {
            Directory.CreateDirectory(corpusDir);
            if (Directory.GetFiles(corpusDir).Length >= 7)
                return;

            var rnd = new Random(2026);
            File.WriteAllBytes(Path.Combine(corpusDir, "01_natural_text.txt"), MakeSample(0, 16384, rnd));
            File.WriteAllBytes(Path.Combine(corpusDir, "02_source_code.cs"), MakeSample(1, 16384, rnd));
            File.WriteAllBytes(Path.Combine(corpusDir, "03_structured.csv"), MakeSample(2, 16384, rnd));
            File.WriteAllBytes(Path.Combine(corpusDir, "04_raster_image.pgm"), MakeSample(3, 16384, rnd));
            File.WriteAllBytes(Path.Combine(corpusDir, "05_executable.bin"), MakeSample(4, 16384, rnd));
            File.WriteAllBytes(Path.Combine(corpusDir, "06_already_compressed.bin"), MakeSample(5, 16384, rnd));
            File.WriteAllBytes(Path.Combine(corpusDir, "07_synthetic_zeros.bin"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(corpusDir, "08_synthetic_noise.bin"), MakeSample(6, 16384, rnd));
        }

        static void EnsureDataset(string dir, int countPerGroup, int seed)
        {
            Directory.CreateDirectory(dir);
            string labelsPath = Path.Combine(dir, "labels.csv");
            if (File.Exists(labelsPath) && new FileInfo(labelsPath).Length > 10)
                return;

            var rnd = new Random(seed);
            var csv = new StringBuilder();
            for (int group = 0; group < 7; group++)
            {
                for (int idx = 0; idx < countPerGroup; idx++)
                {
                    string fileName = $"sample_g{group}_{idx:D2}.dat";
                    byte[] content = MakeSample(group, 2048 + rnd.Next(1024), rnd);
                    File.WriteAllBytes(Path.Combine(dir, fileName), content);
                    csv.AppendLine($"{fileName},{group}");
                }
            }
            File.WriteAllText(labelsPath, csv.ToString(), Encoding.UTF8);
        }

        // Створює характерний файл для кожної з 7 груп корпусу
        public static byte[] MakeSample(int group, int size, Random rnd)
        {
            switch (group)
            {
                case 0: // Природний текст
                    {
                        string[] phrases =
                        {
                        "Алгоритми стиснення даних використовують статистичну надлишковість тексту. ",
                        "Natural language text has predictable character frequencies and bigrams. ",
                        "Кодування Гаффмана та розбір LZ77 дозволяють суттєво зменшити обсяг файлу. "
                    };
                        var sb = new StringBuilder(size);
                        while (sb.Length < size)
                            sb.Append(phrases[rnd.Next(phrases.Length)]);
                        return Encoding.UTF8.GetBytes(sb.ToString(0, size));
                    }
                case 1: // Вихідний код
                    {
                        var sb = new StringBuilder(size);
                        int idx = 0;
                        while (sb.Length < size)
                        {
                            sb.AppendLine($"public int ComputeValue_{idx % 5}(int x) {{ return (x * {idx}) + 42; }}");
                            idx++;
                        }
                        return Encoding.ASCII.GetBytes(sb.ToString(0, size));
                    }
                case 2: // Структуровані дані (CSV)
                    {
                        var sb = new StringBuilder(size);
                        sb.AppendLine("id,timestamp,sensor_a,sensor_b,status");
                        int row = 1;
                        while (sb.Length < size)
                        {
                            sb.AppendLine($"{row},2026-11-11T10:{row % 60:D2}:00,{100 + (row % 15)},{200 + (row % 5)},OK");
                            row++;
                        }
                        return Encoding.ASCII.GetBytes(sb.ToString(0, size));
                    }
                case 3: // Растр без стиснення (плавний градієнт пікселів)
                    {
                        var data = new byte[size];
                        for (int i = 0; i < size; i++)
                            data[i] = (byte)((i / 64 + (i % 64) * 2) & 0xFF);
                        return data;
                    }
                case 4: // Виконуваний файл (машинні коди + вирівнювання нулями)
                    {
                        var data = new byte[size];
                        byte[] opcodes = { 0x55, 0x48, 0x89, 0xE5, 0x48, 0x83, 0xEC, 0x20, 0x89, 0x7D, 0xFC, 0xC3, 0x00, 0x00, 0x00, 0x00 };
                        for (int i = 0; i < size; i++)
                            data[i] = i % 32 < 16 ? opcodes[i % 16] : (byte)rnd.Next(0, 32);
                        return data;
                    }
                case 5: // Вже стиснуте (висока ентропія)
                case 6: // Штучний випадковий шум
                default:
                    {
                        var data = new byte[size];
                        rnd.NextBytes(data);
                        return data;
                    }
            }
        }
    }
}