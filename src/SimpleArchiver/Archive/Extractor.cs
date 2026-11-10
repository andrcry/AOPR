namespace SimpleArchiver
{
    // Записує розпаковані файли на диск. Спершу в тимчасову папку, потім переносить.
    // Наявні файли не перезаписує
    public static class Extractor
    {
        // Записує файли в папку, не перезаписуючи наявні
        public static void SaveFiles(List<ArchiveFile> files, string folder)
        {
            string root = Path.GetFullPath(folder);
            Directory.CreateDirectory(root);

            // Усі перевірки до першого запису
            foreach (ArchiveFile file in files)
            {
                if (!SafeNames.IsSafe(file.Name))
                    throw new ArchiveException(
                        ErrorCodes.Payload,
                        $"небезпечне ім'я файла: {file.Name}"
                    );

                string target = Path.Combine(root, file.Name);
                if (File.Exists(target) || Directory.Exists(target))
                    throw new IOException($"файл уже існує, перезаписувати не будемо: {file.Name}");
            }

            string temp = Path.Combine(root, ".sarc-tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                for (int i = 0; i < files.Count; i++)
                    File.WriteAllBytes(Path.Combine(temp, i.ToString()), files[i].Data);

                for (int i = 0; i < files.Count; i++)
                    File.Move(Path.Combine(temp, i.ToString()), Path.Combine(root, files[i].Name));
            }
            finally
            {
                try
                {
                    Directory.Delete(temp, true);
                }
                catch (IOException)
                {
                    Console.WriteLine($"Не вдалося видалити тимчасову папку {temp}.");
                }
            }
        }
    }
}
