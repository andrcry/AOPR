namespace SimpleArchiver
{
    // Один навчальний приклад: файл, його ознаки і правильний тип (0-6)
    public class Sample
    {
        public string Name { get; set; } = ""; // ім'я файла
        public double[] Features { get; set; } = new double[0]; // ознаки файла
        public int Label { get; set; } // правильний тип файла 0-6

        // Створює приклад з іменем, ознаками і типом
        public Sample(string name, double[] features, int label)
        {
            Name = name;
            Features = features;
            Label = label;
        }
    }
}
