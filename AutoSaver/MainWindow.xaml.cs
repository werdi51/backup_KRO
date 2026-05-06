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

                    // Выводим данные в интерфейс
                    TxtSourcePath.Text = _settings.SourcePath;
                    TxtDestPath.Text = _settings.DestinationPath;
                    TxtStatus.Text = "Настройки загружены.";
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Ошибка загрузки настроек: {ex.Message}";
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
                TxtStatus.Text = $"Настройки успешно сохранены в {DateTime.Now:HH:mm:ss}";
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
            TxtStatus.Clear();

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
                await Task.Run(() => BackupEngine.ExecuteBackupAsync(_settings, progress, msg =>
                    Dispatcher.Invoke(() => {
                        // ДОБАВЛЯЕМ СТРОКУ, А НЕ ЗАМЕНЯЕМ
                        TxtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");

                        // АВТО-СКРОЛЛ К ПОСЛЕДНЕЙ ЗАПИСИ
                        TxtStatus.ScrollToEnd();
                    })));
            }
            catch (Exception ex)
            {
                TxtStatus.AppendText($"\n[ОШИБКА] {ex.Message}\n");
            }
            finally
            {
                BtnRunNow.IsEnabled = true;
                // Сброс показателей прогресса (как обсуждали раньше)
            }
        }
    }
}