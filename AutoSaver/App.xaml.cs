using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;


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
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string settingsPath = Path.Combine(baseDir, "settings.json");
            string logPath = Path.Combine(baseDir, "service_log.txt"); // Файл для отладки

            if (File.Exists(settingsPath))
            {
                try
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now}] Запуск сервиса...\n");

                    string json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<BackupSettings>(json);

                    if (settings != null)
                    {
                        // Запускаем через Task.Run, чтобы избежать блокировки потока
                        Task.Run(async () =>
                        {
                            await BackupEngine.ExecuteBackupAsync(
                                settings,
                                new Progress<BackupProgressData>(),
                                msg => File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n")
                            );
                        }).GetAwaiter().GetResult(); // Ждем завершения без дедлока

                        // Сохраняем настройки
                        string updatedJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(settingsPath, updatedJson);

                        new ToastContentBuilder().AddText("AutoSaver").AddText("Бэкап успешно завершен").Show();
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now}] КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}\n");
                    new ToastContentBuilder().AddText("AutoSaver: ОШИБКА").AddText(ex.Message).Show();
                }
            }
        }



    }

}
