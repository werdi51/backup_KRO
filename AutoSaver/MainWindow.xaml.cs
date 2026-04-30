using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
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

        private void BtnRunNow_Click(object sender, RoutedEventArgs e)
        {
            // Блокируем кнопку, чтобы пользователь не нажал дважды во время работы
            BtnRunNow.IsEnabled = false;
            TxtStatus.Text = "Запуск... Пожалуйста, подождите.";

            try
            {
                // Вызываем наш движок
                // msg => TxtStatus.Text = msg — это способ передавать сообщения из движка прямо в интерфейс
                BackupEngine.ExecuteBackup(_settings, msg => {
                    TxtStatus.Text = msg;
                });

                // После завершения бэкапа сохраняем обновленные даты (LastFullBackupDate и т.д.)
                BtnSave_Click(null, null);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Критическая ошибка: {ex.Message}";
            }
            finally
            {
                BtnRunNow.IsEnabled = true;
            }
        }
    }
}