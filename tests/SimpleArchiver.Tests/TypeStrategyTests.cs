namespace SimpleArchiver.Tests
{
    // Підбір параметрів стиснення за типом файла
    public class TypeStrategyTests
    {
        // Таблиця стратегій має запис для кожного з 7 типів, вже стиснуте не стискається
        [Fact]
        public void DefaultsCoverAllTypes()
        {
            TypeStrategy[] strategies = TypeStrategy.Defaults();

            Assert.Equal(FileClassifier.ClassCount, strategies.Length);
            Assert.False(strategies[FileClassifier.AlreadyCompressed].Compress);

            foreach (TypeStrategy strategy in strategies)
            {
                Assert.True(strategy.MaxChain > 0);
                Assert.True(strategy.BlockSize > 0);
            }
        }

        // Стратегія змінює лише свої параметри і не чіпає загальні налаштування
        [Fact]
        public void ApplyToChangesOnlyStrategyFields()
        {
            var settings = new Settings();
            settings.MaxVolumeBytes = 5000;
            var strategy = new TypeStrategy(true, ParserKind.Greedy, 8, 32768);

            Settings fileSettings = strategy.ApplyTo(settings);

            Assert.Equal(ParserKind.Greedy, fileSettings.Parser);
            Assert.Equal(8, fileSettings.MaxChain);
            Assert.Equal(32768, fileSettings.BlockSize);
            Assert.Equal(5000, fileSettings.MaxVolumeBytes);

            Assert.Equal(ParserKind.Lazy, settings.Parser);
            Assert.Equal(64, settings.MaxChain);
            Assert.Equal(65536, settings.BlockSize);
        }

        // Архіватор бере розбір за типом файла, записує його в поле strategy, і файли відновлюються
        [Fact]
        public void ArchiveUsesStrategyOfEachType()
        {
            string path = Path.GetTempFileName();
            SyntheticFiles.TrainClassifier().Save(path);

            try
            {
                var settings = new Settings();
                settings.ClassifierPath = path;
                settings.UseTypeStrategy = true;

                var input = new List<ArchiveFile>();
                input.Add(new ArchiveFile("text.txt", SyntheticFiles.Text(8000, 60)));
                input.Add(new ArchiveFile("photo.jpg", TestHelpers.RandomBytes(8000, 61)));
                input.Add(new ArchiveFile("zeros.bin", SyntheticFiles.Synthetic(8000, 63)));

                var salt = new byte[16];
                List<byte[]> volumes = ArchiveWriter.CreateArchive(
                    input,
                    TestHelpers.Password,
                    settings,
                    salt,
                    new byte[8]
                );

                VolumeHeader header = VolumeHeader.Parse(volumes[0]);
                Payload payload = Payload.Parse(
                    Volume.Open(volumes[0], header, Keys.Derive(TestHelpers.Password, salt))
                );

                // Текст - тип 0, лінивий розбір; шум - тип 5, без стиснення; нулі - тип 6, жадібний розбір
                Assert.Equal(0, payload.Files[0].FileType);
                Assert.Equal((int)ParserKind.Lazy, payload.Files[0].Strategy);
                Assert.Equal(FileClassifier.AlreadyCompressed, payload.Files[1].FileType);
                Assert.Equal(0, payload.Files[1].Strategy);
                Assert.Equal(6, payload.Files[2].FileType);
                Assert.Equal((int)ParserKind.Greedy, payload.Files[2].Strategy);

                List<ArchiveFile> output = ArchiveReader.ReadArchive(volumes, TestHelpers.Password);
                for (int i = 0; i < input.Count; i++)
                    Assert.Equal(input[i].Data, output[i].Data);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
