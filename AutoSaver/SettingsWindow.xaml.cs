using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AutoSaver
{
    public partial class SettingsWindow : Window
    {
        public int FullBackupDay { get; private set; }
        public int DailyRetentionDays { get; private set; }

        public SettingsWindow(int fullBackupDay, int retentionDays)
        {
            InitializeComponent();

            for (int i = 1; i <= 31; i++)
                CmbDay.Items.Add(i);

            CmbDay.SelectedItem = Math.Clamp(fullBackupDay, 1, 31);
            TxtDays.Text = Math.Clamp(retentionDays, 1, 365).ToString();
        }

        // Только цифры
        private void TxtDays_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtDays.Text, out int days) || days < 1 || days > 365)
            {
                MessageBox.Show("Срок хранения должен быть числом от 1 до 365.");
                return;
            }
            if (days > 20)
            {
                MessageBox.Show("Данные сохранены но, при нехватке места на диске программа может работать некорректно.");
            }

            FullBackupDay = (int)CmbDay.SelectedItem;
            DailyRetentionDays = days;
            DialogResult = true;
        }
    }
}