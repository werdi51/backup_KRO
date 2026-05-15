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
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            string logFilePath = GetLogFilePath("service");
            if (File.Exists(logFilePath))
                File.Delete(logFilePath);

            if (!File.Exists(settingsPath))
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now}] Файл настроек не найден: {settingsPath}\n");
                new ToastContentBuilder().AddText("AutoSaver: ОШИБКА").AddText("Файл настроек не найден. Бэкап не выполнен.").Show();
                return;
            }

            try
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now}] Запуск сервиса...\n");
                string json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<BackupSettings>(json);

                if (settings == null)
                {
                    File.AppendAllText(logFilePath, $"[{DateTime.Now}] Ошибка: настройки не десериализованы.\n");
                    new ToastContentBuilder().AddText("AutoSaver: ОШИБКА").AddText("Не удалось прочитать настройки. Проверьте settings.json.").Show();
                    return;
                }

                // --- ПРОВЕРКА ПУТЕЙ ---
                if (string.IsNullOrWhiteSpace(settings.SourcePath) || string.IsNullOrWhiteSpace(settings.DestinationPath))
                {
                    string errMsg = "Путь к исходной папке или папке бэкапов не задан.";
                    File.AppendAllText(logFilePath, $"[{DateTime.Now}] {errMsg}\n");
                    new ToastContentBuilder().AddText("AutoSaver: ОШИБКА").AddText(errMsg).Show();
                    return;
                }

                if (!Directory.Exists(settings.SourcePath))
                {
                    string errMsg = $"Исходная папка не существует: {settings.SourcePath}";
                    File.AppendAllText(logFilePath, $"[{DateTime.Now}] {errMsg}\n");
                    new ToastContentBuilder().AddText("AutoSaver: ОШИБКА").AddText(errMsg).Show();
                    return;
                }

                // Запускаем бэкап
                Task.Run(async () =>
                {
                    await BackupEngine.ExecuteBackupAsync(
                        settings,
                        new Progress<BackupProgressData>(),
                        msg => File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n")
                    );
                }).GetAwaiter().GetResult();

                // Сохраняем настройки
                string updatedJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, updatedJson);

                new ToastContentBuilder().AddText("AutoSaver").AddText("Бэкап успешно завершен").Show();
            }
            catch (Exception ex)
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now}] КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}\n");
                new ToastContentBuilder().AddText("AutoSaver: ОШИБКА").AddText(ex.Message).Show();
            }
        }
        private string GetLogFilePath(string prefix)
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            string fileName = $"{prefix}_{DateTime.Now:yyyy-MM-dd}.log";
            return Path.Combine(logDir, fileName);
        }



    }

    }
