using System.Text;

namespace SimpleArchiver.Tests
{
    // Просунуті рівні: ДП-розбір LZ77, ДП-розбиття на блоки, класифікатор типу файла, прогноз байта
    public class Stage3Tests
    {
        // ДП-розбір за фіксованими вартостями

        // Різні дані для перевірки розборів
        static List<byte[]> ParserSamples()
        {
            var samples = new List<byte[]>();
            samples.Add(TestHelpers.Hello480());
            samples.Add(
                Encoding.ASCII.GetBytes("xabQQQQabcdefgh##xabcdefgh, abcabcabcabc abcdefgh")
            );
            samples.Add(SyntheticFiles.Text(5000, 1));
            samples.Add(SyntheticFiles.Csv(5000, 2));
            samples.Add(TestHelpers.RandomBytes(3000, 3));
            samples.Add(new byte[] { 7, 7 });
            return samples;
        }

        // Кейс D1 номер 13: вартість ДП-розбору не більша за жадібний і лінивий
        [Fact]
        public void DpParseCostNeverHigherThanGreedyOrLazy()
        {
            foreach (byte[] data in ParserSamples())
            {
                List<Token> dp = Lz77.ParseDp(data, 64);

                Assert.True(Lz77.FixedCost(dp) <= Lz77.FixedCost(Lz77.ParseGreedy(data, 64)));
                Assert.True(Lz77.FixedCost(dp) <= Lz77.FixedCost(Lz77.ParseLazy(data, 64)));
                Assert.Equal(data, BlockCoder.Decode(BlockCoder.Encode(dp), data.Length));
            }
        }

        // Архів із ДП-розбором відновлює файли
        [Fact]
        public void DpParseArchiveRestoresFiles()
        {
            var settings = new Settings();
            settings.Parser = ParserKind.Dp;

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("text.txt", SyntheticFiles.Text(20000, 4)),
                TestHelpers.Password,
                settings
            );

            Assert.Equal(
                SyntheticFiles.Text(20000, 4),
                ArchiveReader.ReadArchive(volumes, TestHelpers.Password)[0].Data
            );
        }

        // ДП-розбиття на блоки

        // Текст, шум і нулі в одному файлі: різні частини краще стискаються окремими блоками
        static byte[] MixedData()
        {
            var data = new byte[20000];
            byte[] text = SyntheticFiles.Text(7000, 5);
            byte[] noise = TestHelpers.RandomBytes(7000, 6);
            Array.Copy(text, 0, data, 0, 7000);
            Array.Copy(noise, 0, data, 7000, 7000);
            return data; // останні 6000 байтів - нулі
        }

        // Налаштування для ДП-розбиття
        static Settings SplitSettings()
        {
            var settings = new Settings();
            settings.BlockSize = 8192;
            settings.SplitUnit = 1024;
            return settings;
        }

        // Кейс D1 номер 14: ДП-розбиття дає розмір не більший, ніж розбиття фіксованим блоком
        [Fact]
        public void DpSplitNeverBiggerThanFixedBlocks()
        {
            var samples = new List<byte[]>();
            samples.Add(MixedData());
            samples.Add(SyntheticFiles.Text(30000, 7));
            samples.Add(TestHelpers.RandomBytes(10000, 8));
            samples.Add(new byte[0]);

            foreach (byte[] data in samples)
            {
                long fixedSize = BlockSplitter.TotalSize(
                    BlockSplitter.SplitFixed(data, SplitSettings(), true)
                );
                long dpSize = BlockSplitter.TotalSize(BlockSplitter.SplitDp(data, SplitSettings()));
                Assert.True(dpSize <= fixedSize);
            }
        }

        // Архів із ДП-розбиттям відновлює файли
        [Fact]
        public void DpSplitArchiveRestoresFiles()
        {
            Settings settings = SplitSettings();
            settings.UseDpBlockSplit = true;

            List<byte[]> volumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("mixed.bin", MixedData()),
                TestHelpers.Password,
                settings
            );

            Assert.Equal(
                MixedData(),
                ArchiveReader.ReadArchive(volumes, TestHelpers.Password)[0].Data
            );
        }

        // BlockSize має ділитися на SplitUnit
        [Fact]
        public void DpSplitBlockSizeMustBeMultipleOfUnit()
        {
            var settings = new Settings();
            settings.BlockSize = 10000;
            settings.SplitUnit = 3000;

            bool rejected = false;
            try
            {
                BlockSplitter.SplitDp(MixedData(), settings);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Assert.True(rejected);
        }

        // Ознаки файла

        // Ознаки мають очікувані значення
        [Fact]
        public void FeaturesHaveExpectedValues()
        {
            Assert.Equal(52, FileFeatures.Compute(SyntheticFiles.Text(1000, 1)).Length);

            // Однакові байти - ентропія 0; усі 256 значень порівну - ентропія 8 бітів
            Assert.Equal(0.0, FileFeatures.ByteEntropy(new byte[1000]));

            var allBytes = new byte[256 * 4];
            for (int i = 0; i < allBytes.Length; i++)
                allBytes[i] = (byte)i;
            Assert.Equal(8.0, FileFeatures.ByteEntropy(allBytes), 6);

            Assert.Equal(
                1.0,
                FileFeatures.PrintableShare(Encoding.ASCII.GetBytes("Hello, world!\n"))
            );

            // Порожній файл - усі ознаки нульові
            foreach (double feature in FileFeatures.Compute(new byte[0]))
                Assert.Equal(0.0, feature);
        }

        // Перцептрон

        // Градієнт перцептрона збігається з чисельним
        [Fact]
        public void MlpGradientMatchesNumeric()
        {
            var net = new Mlp(new[] { FileFeatures.Count, 6, FileClassifier.ClassCount }, 1);
            double[] x = FileFeatures.Compute(SyntheticFiles.Csv(2000, 1));
            var target = new double[FileClassifier.ClassCount];
            target[2] = 1;

            Assert.True(net.GradientCheck(x, target) < 1e-7);
        }

        // Macro-F1 для відомої матриці помилок
        [Fact]
        public void MacroF1KnownMatrix()
        {
            var confusion = new int[FileClassifier.ClassCount, FileClassifier.ClassCount];
            confusion[0, 0] = 8;
            confusion[0, 1] = 2;
            confusion[1, 0] = 1;
            confusion[1, 1] = 9;

            // F1 класу 0 = 16/19, класу 1 = 18/21, решти класів немає
            Assert.Equal((16.0 / 19 + 18.0 / 21) / 2, Metrics.MacroF1(confusion), 9);
        }

        // Класифікатор розпізнає штучні типи файлів
        [Fact]
        public void ClassifierLearnsSyntheticTypes()
        {
            FileClassifier classifier = SyntheticFiles.TrainClassifier();

            List<Sample> test = SyntheticFiles.MakeSamples(10, 1000);
            double f1 = Metrics.MacroF1(Metrics.Confusion(classifier.Net, test));

            Assert.True(f1 >= 0.9, $"macro-F1 = {f1}");
        }

        // Збережені й завантажені ваги дають ті самі відповіді
        [Fact]
        public void ClassifierSaveAndLoadGiveSameAnswers()
        {
            FileClassifier classifier = SyntheticFiles.TrainClassifier();
            string path = Path.GetTempFileName();

            try
            {
                classifier.Save(path);
                FileClassifier loaded = FileClassifier.Load(path);

                foreach (Sample sample in SyntheticFiles.MakeSamples(3, 2000))
                    Assert.Equal(
                        classifier.Net.PredictClass(sample.Features),
                        loaded.Net.PredictClass(sample.Features)
                    );
            }
            finally
            {
                File.Delete(path);
            }
        }

        // Вже стиснутий файл записується без стиснення
        [Fact]
        public void ClassifierAlreadyCompressedFileIsStoredWithoutCompression()
        {
            string path = Path.GetTempFileName();
            SyntheticFiles.TrainClassifier().Save(path);

            try
            {
                var settings = new Settings();
                settings.ClassifierPath = path;

                var input = new List<ArchiveFile>();
                input.Add(new ArchiveFile("text.txt", SyntheticFiles.Text(8000, 50)));
                input.Add(new ArchiveFile("photo.jpg", TestHelpers.RandomBytes(8000, 51)));

                var salt = new byte[16];
                List<byte[]> volumes = ArchiveWriter.CreateArchive(
                    input,
                    TestHelpers.Password,
                    settings,
                    salt,
                    new byte[8]
                );

                // Розшифровуємо том і дивимося, що записав класифікатор
                VolumeHeader header = VolumeHeader.Parse(volumes[0]);
                Payload payload = Payload.Parse(
                    Volume.Open(volumes[0], header, Keys.Derive(TestHelpers.Password, salt))
                );

                Assert.Equal(0, payload.Files[0].FileType);
                Assert.Equal(FileClassifier.AlreadyCompressed, payload.Files[1].FileType);
                Assert.Equal(0, payload.Files[1].Strategy);

                List<ArchiveFile> output = ArchiveReader.ReadArchive(volumes, TestHelpers.Password);
                Assert.Equal(input[0].Data, output[0].Data);
                Assert.Equal(input[1].Data, output[1].Data);
            }
            finally
            {
                File.Delete(path);
            }
        }

        // Прогноз наступного байта

        // Плавні дані, як рядок растру: сусідні байти майже однакові
        static byte[] SmoothData(int count)
        {
            var data = new byte[count];
            for (int i = 0; i < count; i++)
                data[i] = (byte)(128 + 100 * Math.Sin(i / 40.0) + 20 * Math.Sin(i / 7.0));
            return data;
        }

        // Із залишків прогнозу відновлюються ті самі дані
        [Fact]
        public void PredictorResidualsRestoreData()
        {
            var samples = new List<byte[]>();
            samples.Add(SmoothData(5000));
            samples.Add(TestHelpers.RandomBytes(3000, 9));
            samples.Add(SyntheticFiles.Text(3000, 10));
            samples.Add(new byte[] { 1 });

            foreach (byte[] data in samples)
            {
                BytePredictor predictor = BytePredictor.Train(data, 4);
                Assert.Equal(data, predictor.Restore(predictor.MakeResiduals(data)));
            }
        }

        // Кейси D1 номер 15 і номер 16: з прогнозом файл відновлюється, а на плавних даних архів менший
        [Fact]
        public void PredictorSmallerArchiveOnSmoothData()
        {
            byte[] data = SmoothData(60000);

            var plain = new Settings();
            var predicted = new Settings();
            predicted.UsePrediction = true;

            List<byte[]> plainVolumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("row.bmp", data),
                TestHelpers.Password,
                plain
            );
            List<byte[]> predictedVolumes = ArchiveWriter.CreateArchive(
                TestHelpers.OneFile("row.bmp", data),
                TestHelpers.Password,
                predicted
            );

            Assert.True(predictedVolumes[0].Length < plainVolumes[0].Length);
            Assert.Equal(
                data,
                ArchiveReader.ReadArchive(predictedVolumes, TestHelpers.Password)[0].Data
            );
        }

        // Неправильний заголовок прогнозу відхиляється
        [Fact]
        public void PredictorBadHeaderIsRejected()
        {
            byte[] data = Encoding.ASCII.GetBytes("abc");

            var file = new FileRecord();
            file.Name = "abc.txt";
            file.OriginalSize = data.Length;
            file.Crc = Crc32.Compute(data);

            var block = new BlockRecord();
            block.RawLength = data.Length;
            block.Data = new byte[] { 200, 0, 0 }; // k = 200 - більше за дозволені 16

            var payload = new Payload();
            payload.Flags = Payload.PredictionFlag;
            payload.Files.Add(file);
            payload.Blocks.Add(block);

            byte[] volume = TestHelpers.SealPayload(payload.ToBytes());
            Assert.Equal(
                ErrorCodes.Code,
                TestHelpers.ErrorWhenReading(volume, TestHelpers.Password)
            );
        }

        // Невідомий прапорець тому відхиляється
        [Fact]
        public void PayloadUnknownFlagIsRejected()
        {
            byte[] payload = new Payload().ToBytes();
            payload[2] = 0x02; // невідомий біт flags

            byte[] volume = TestHelpers.SealPayload(payload);
            Assert.Equal(
                ErrorCodes.Payload,
                TestHelpers.ErrorWhenReading(volume, TestHelpers.Password)
            );
        }
    }
}
