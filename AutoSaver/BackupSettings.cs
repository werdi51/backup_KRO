using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSaver
{
    public class BackupSettings
    {

        // Путь к папке которую копируем
        public string SourcePath { get; set; } = string.Empty;

        // Путь где храним бэкапы
        public string DestinationPath { get; set; } = string.Empty;

        // Когда в последний раз делали полный бэкап месяца
        public DateTime LastFullBackupDate { get; set; }

        // Когда в последний раз делали ежедневный инкремент
        public DateTime LastDailyBackupDate { get; set; }

    }
}
