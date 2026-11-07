namespace SimpleArchiver
{
    // Налаштування архіватора. Справжнє значення взяти з team_config.json
    public class Settings
    {
        public int BlockSize { get; set; } = 65536; // block_size: на скільки байтів ріжемо кожен файл
    }
}
