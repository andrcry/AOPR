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
    }
}
