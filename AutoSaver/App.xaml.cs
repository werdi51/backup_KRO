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
            // Отключаем автоматический выход при закрытии окон, пока не решим сами
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (e.Args.Contains("-run"))
            {
                RunSilentBackup();
                this.Shutdown();
            }
            else
            {
                // Возвращаем обычный режим: программа закроется, когда закроешь окно
                this.ShutdownMode = ShutdownMode.OnLastWindowClose;

                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }

        private void RunSilentBackup()
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

            if (File.Exists(settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<BackupSettings>(json);

                    if (settings != null)
                    {
                        // 1. ИСПРАВЛЕНО: Теперь создаем прогресс правильного типа
                        // Мы используем BackupProgressData, но так как в тихом режиме интерфейса нет,
                        // он просто будет принимать данные и ничего с ними не делать.
                        var noProgress = new Progress<BackupProgressData>();

                        // 2. Вызываем асинхронный метод и ждем его завершения
                        BackupEngine.ExecuteBackupAsync(
                            settings,
                            noProgress, // Теперь типы совпадают
                            msg => Console.WriteLine(msg)
                        ).Wait();

                        // 3. Сохраняем обновленные даты
                        string updatedJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(settingsPath, updatedJson);

                        BackupEngine.ExecuteBackupAsync(...).Wait();

                        // Показываем уведомление об успехе
                        MessageBox.Show("Бэкап успешно завершен!", "AutoSaver",
                                        MessageBoxButton.OK, MessageBoxImage.Information);

                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText("error_log.txt", $"{DateTime.Now}: {ex.Message}\n");
                    MessageBox.Show($"Ошибка бэкапа: {ex.Message}", "AutoSaver - ОШИБКА",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


    }

}
