namespace SimpleArchiver
{
    // Матриця помилок і macro-F1 для звіту
    public static class Metrics
    {
        // confusion[справжній тип, передбачений тип] - скільки таких файлів
        public static int[,] Confusion(Mlp net, List<Sample> samples)
        {
            var confusion = new int[FileClassifier.ClassCount, FileClassifier.ClassCount];

            foreach (Sample sample in samples)
                confusion[sample.Label, net.PredictClass(sample.Features)]++;

            return confusion;
        }

        // macro-F1 - середнє F1 по класах. F1 = 2 * P * R / (P + R),
        // P (точність) = TP / (TP + FP), R (повнота) = TP / (TP + FN).
        // Беремо лише класи, які є серед справжніх або передбачених
        public static double MacroF1(int[,] confusion)
        {
            int classes = confusion.GetLength(0);
            double sum = 0;
            int used = 0;

            for (int k = 0; k < classes; k++)
            {
                int tp = confusion[k, k];
                int fp = 0;
                int fn = 0;

                for (int other = 0; other < classes; other++)
                {
                    if (other == k)
                        continue;

                    fp += confusion[other, k];
                    fn += confusion[k, other];
                }

                if (tp + fp + fn == 0)
                    continue; // цього класу немає взагалі

                double precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
                double recall = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
                double f1 =
                    precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);

                sum += f1;
                used++;
            }

            return used == 0 ? 0 : sum / used;
        }

        // Друкує матрицю помилок на екран
        public static void PrintConfusion(int[,] confusion)
        {
            int classes = confusion.GetLength(0);

            Console.WriteLine("Матриця помилок. Рядки - справжні типи, стовпці - передбачені типи");
            string header = "     ";
            for (int k = 0; k < classes; k++)
                header += $"{k, 5}";
            Console.WriteLine(header);

            for (int i = 0; i < classes; i++)
            {
                string line = $"{i, 5}";
                for (int k = 0; k < classes; k++)
                    line += $"{confusion[i, k], 5}";
                Console.WriteLine(line);
            }
        }
    }
}
