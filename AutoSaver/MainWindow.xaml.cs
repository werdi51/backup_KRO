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

        // --- ЛОГИКА ВЫБОРА ПАПОК ---

        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку-источник (сетевую)" };
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

        // --- РАБОТА С НАСТРОЙКАМИ (JSON) ---

        private void LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<BackupSettings>(json) ?? new BackupSettings();

                    // Обнуляем миллисекунды, чтобы избежать дрейфа из-за точности
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
                AppendColoredText($"Настройки успешно сохранены в {DateTime.Now:HH:mm:ss}", Brushes.Green);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить настройки: {ex.Message}");
            }
        }

        // Добавь async перед void
        // BtnRunNow_Click в MainWindow.xaml.cs

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
            var progress = new Progress<BackupProgressData>(data =>
            {
                PrgBar.Value = data.Percentage;
                TxtPercent.Text = $"{data.Percentage:F1}%";
                TxtSpeed.Text = $"{data.Speed:F2} МБ/с";
                TxtTimer.Text = data.TimeElapsed;
                TxtFileCount.Text = $"Файлов: {data.CurrentFileNumber} / {data.TotalFiles}";
                TxtSizeProgress.Text = $"{FormatSize(data.CopiedBytes)} / {FormatSize(data.TotalBytes)}";
            });

            try
            {
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup_history.log");


                    await BackupEngine.ExecuteBackupAsync(_settings, progress, msg =>
                        Dispatcher.Invoke(() =>
                        {
                            if (msg.StartsWith("[ОШИБКА]") || msg.Contains("не удалось") || msg.Contains("ошибка"))
                                AppendColoredText(msg, Brushes.Red);
                            else if (msg.StartsWith("[ПРОПУЩЕНО]") || msg.Contains("ПРЕДУПРЕЖДЕНИЕ"))
                                AppendColoredText(msg, Brushes.Orange);
                            else
                                AppendColoredText(msg, Brushes.Black);
                            // также пишем в файл (без цвета)
                            File.AppendAllText(logFilePath, msg + "\n");
                        }));


                // 💾 СОХРАНЯЕМ ОБНОВЛЁННЫЕ ДАТЫ ПОСЛЕ БЭКАПА
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                AppendColoredText($"[{DateTime.Now:HH:mm:ss}] Настройки с обновлёнными датами сохранены.", Brushes.Green);
            }
            catch (Exception ex)
            {
                AppendColoredText($"[ОШИБКА] {ex.Message}", Brushes.Red);
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
            }
        }
        private void AppendColoredText(string text, Brush color)
        {
            var paragraph = new Paragraph();
            var run = new Run(text + "\n") { Foreground = color };
            paragraph.Inlines.Add(run);
            TxtStatus.Document.Blocks.Add(paragraph);
            TxtStatus.ScrollToEnd();
        }
        private void SetStatusText(string text, Brush color)
        {
            TxtStatus.Document.Blocks.Clear();
            var paragraph = new Paragraph();
            var run = new Run(text) { Foreground = color };
            paragraph.Inlines.Add(run);
            TxtStatus.Document.Blocks.Add(paragraph);
            TxtStatus.ScrollToEnd();
        }
    }

    }
