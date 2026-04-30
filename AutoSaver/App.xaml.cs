using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace AutoSaver
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        protected override void OnStartup(StartupEventArgs e)
        {
            // Проверяем, есть ли аргумент "-run"
            if (e.Args.Contains("-run"))
            {
                RunSilentBackup();
                Shutdown(); // Закрываем программу сразу после работы
            }
            else
            {
                // Если аргументов нет, запускаем обычное окно
                base.OnStartup(e);
            }
        }

        private void RunSilentBackup()
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

            if (File.Exists(settingsPath))
            {
                try
                {
                    // Загружаем настройки
                    string json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<BackupSettings>(json);

                    if (settings != null)
                    {
                        // Запускаем бэкап без интерфейса (логи пишем просто в консоль или игнорируем)
                        BackupEngine.ExecuteBackup(settings, msg => Console.WriteLine(msg));

                        // Сохраняем обновленные даты
                        string updatedJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(settingsPath, updatedJson);
                    }
                }
                catch (Exception ex)
                {
                    // В тихом режиме ошибки лучше писать в лог-файл
                    File.AppendAllText("error_log.txt", $"{DateTime.Now}: {ex.Message}\n");
                }
            }
        }

    }

}
