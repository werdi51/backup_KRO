using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Shell;

namespace AutoSaver
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private BackupSettings _settings = new BackupSettings();

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        // Выыбор папок

        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку-источник" };
            if (dialog.ShowDialog() == true)
            {
                TxtSourcePath.Text = dialog.FolderName;
            }
        }

        private void BtnBrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку для хранения бэкапов" };
            if (dialog.ShowDialog() == true)
            {
                TxtDestPath.Text = dialog.FolderName;
            }
        }

        // Файл настроек

        private void LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<BackupSettings>(json) ?? new BackupSettings();

                    _settings.LastFullBackupDate = _settings.LastFullBackupDate.AddTicks(
                        -(_settings.LastFullBackupDate.Ticks % TimeSpan.TicksPerSecond));
                    _settings.LastDailyBackupDate = _settings.LastDailyBackupDate.AddTicks(
                        -(_settings.LastDailyBackupDate.Ticks % TimeSpan.TicksPerSecond));

                    TxtSourcePath.Text = _settings.SourcePath;
                    TxtDestPath.Text = _settings.DestinationPath;
                    TxtStatus.Document.Blocks.Clear();
                    TxtStatus.Document.Blocks.Add(new Paragraph(new Run("Настройки загружены.") { Foreground = Brushes.Gray }));
                }
                catch (Exception ex)
                {
                    AppendColoredText($"Ошибка загрузки настроек: {ex.Message}", Brushes.Red);
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _settings.SourcePath = TxtSourcePath.Text;
            _settings.DestinationPath = TxtDestPath.Text;

            try
            {
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                AppendColoredText($"Настройки сохранены в {DateTime.Now:HH:mm:ss}", Brushes.Green);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить настройки: {ex.Message}");
            }
        }

        private string FormatSize(long bytes)
        {
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            if (gb >= 1) return $"{gb:F2} ГБ";
            double mb = bytes / 1024.0 / 1024.0;
            return $"{mb:F2} МБ";
        }
        private async void BtnRunNow_Click(object sender, RoutedEventArgs e)
        {
            BtnRunNow.IsEnabled = false;
            TxtStatus.Document.Blocks.Clear();

            string logFilePath = GetLogFilePath("backup");
            if (File.Exists(logFilePath))
                File.Delete(logFilePath);


            // П бар
            TaskbarItem.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
            TaskbarItem.ProgressValue = 0;
            TaskbarItem.Description = "Выполняется бэкап...";

            var progress = new Progress<BackupProgressData>(data =>
            {
                double safePercent = double.IsNaN(data.Percentage) || double.IsInfinity(data.Percentage)
                    ? 0
                    : Math.Max(0, Math.Min(100, data.Percentage));

                double safeSpeed = double.IsNaN(data.Speed) || double.IsInfinity(data.Speed)
                    ? 0
                    : data.Speed;

                PrgBar.Value = safePercent;
                TxtPercent.Text = $"{safePercent:F1}%";
                TxtSpeed.Text = $"{safeSpeed:F2} МБ/с";
                TxtTimer.Text = data.TimeElapsed ?? "00:00:00";
                TxtFileCount.Text = $"Файлов: {data.CurrentFileNumber} / {data.TotalFiles}";
                TxtSizeProgress.Text = $"{FormatSize(data.CopiedBytes)} / {FormatSize(data.TotalBytes)}";

                TaskbarItem.ProgressValue = safePercent / 100.0;
            });

            try
            {
                //Progress<T> - мост для потоков
                await Task.Run(() => BackupEngine.ExecuteBackupAsync(_settings, progress, msg =>
                    Dispatcher.Invoke(() =>
                    {
                        //проверка
                        bool isFileOk = msg.StartsWith("[OK]") || msg.StartsWith("[OK инкремент]");
                        if (isFileOk)
                        {
                            string fileName = msg.Replace("[OK] ", "").Replace("[OK инкремент] ", "");
                            SetCurrentFile(fileName);
                        }
                        else
                        {
                            if (msg.Contains("ошибка") || msg.Contains("не удалось") || msg.Contains("ОШИБКА"))
                                AppendColoredText(msg, Brushes.Red);
                            else if (msg.Contains("успешно") ||
                            msg.Contains("завершён") || msg.Contains("ИТОГО"))
                                AppendColoredText(msg, Brushes.Green);
                            else
                                AppendColoredText(msg, Brushes.Black);
                        }
                        File.AppendAllText(logFilePath, msg + "\n");
                    })));
                //конвертация
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                AppendColoredText($"[{DateTime.Now:HH:mm:ss}] Настройки с обновлёнными датами сохранены.", Brushes.Green);

                TaskbarItem.ProgressValue = 1;
                TaskbarItem.Description = "Бэкап завершён";
                await Task.Delay(2000);
                TaskbarItem.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                TaskbarItem.Description = "AutoSaver";
            }
            catch (Exception ex)
            {
                //хз, исправь
                AppendColoredText($"[ОШИБКА] {ex.Message}", Brushes.Red);
                TaskbarItem.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Error;
                TaskbarItem.Description = "Ошибка бэкапа";
                await Task.Delay(5000);
                TaskbarItem.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                TaskbarItem.Description = "AutoSaver";
            }
            finally
            {
                BtnRunNow.IsEnabled = true;
                PrgBar.Value = 0;
                TxtPercent.Text = "0%";
                TxtSpeed.Text = "0 МБ/с";
                TxtTimer.Text = "00:00:00";
                TxtFileCount.Text = "Файлов: 0 / 0";
                TxtSizeProgress.Text = "0.00 ГБ / 0.00 ГБ";
                SetCurrentFile("—");
            }
        }
        private void SetCurrentFile(string fileName)
        {

            TxtCurrentFile.Text = $"Текущий файл: {fileName}";

        }
        private void AppendColoredText(string text, Brush color)
        {
            var paragraph = new Paragraph();
            var run = new Run(text + "\n") { Foreground = color };
            paragraph.Inlines.Add(run);
            TxtStatus.Document.Blocks.Add(paragraph);
            TxtStatus.ScrollToEnd();
        }
        private string GetLogFilePath(string prefix)
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            string fileName = $"{prefix}_{DateTime.Now:yyyy-MM-dd}.log";
            return Path.Combine(logDir, fileName);
        }
        //private void SetStatusText(string text, Brush color)
        //{
        //    TxtStatus.Document.Blocks.Clear();
        //    var paragraph = new Paragraph();
        //    var run = new Run(text) { Foreground = color };
        //    paragraph.Inlines.Add(run);
        //    TxtStatus.Document.Blocks.Add(paragraph);
        //    TxtStatus.ScrollToEnd();
        //}
    }

    }
