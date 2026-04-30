using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSaver
{
    public static class BackupEngine
    {
        public static void ExecuteBackup(BackupSettings settings, Action<string> logger)
        {
            if (string.IsNullOrEmpty(settings.SourcePath) || string.IsNullOrEmpty(settings.DestinationPath))
            {
                logger("Ошибка: Пути не настроены!");
                return;
            }

            // 1. Создаем структуру папок
            string rootDir = settings.DestinationPath;
            string monthlyDir = Path.Combine(rootDir, "Month_Full");
            string dailyRootDir = Path.Combine(rootDir, "Daily_Changes");

            Directory.CreateDirectory(monthlyDir);
            Directory.CreateDirectory(dailyRootDir);

            DateTime now = DateTime.Now;

            // 2. Логика ОБНОВЛЕНИЯ МЕСЯЦА (Раз в месяц или если папка пуста)
            // Проверяем, наступил ли новый месяц с момента последнего полного бэкапа
            if (settings.LastFullBackupDate.Month != now.Month || !Directory.EnumerateFileSystemEntries(monthlyDir).Any())
            {
                logger("Наступил новый месяц. Обновляем полную копию...");
                ClearDirectory(monthlyDir);
                CopyDirectory(settings.SourcePath, monthlyDir, true); // Копируем всё
                settings.LastFullBackupDate = now;
            }

            // 3. Логика ЕЖЕДНЕВНОГО ИНКРЕМЕНТА
            string todayFolder = Path.Combine(dailyRootDir, now.ToString("yyyy-MM-dd"));

            if (!Directory.Exists(todayFolder))
            {
                logger($"Создаем инкрементальный бэкап за {now:dd.MM.yyyy}...");
                Directory.CreateDirectory(todayFolder);

                // Копируем только то, что изменилось с момента последнего бэкапа
                CopyModifiedFiles(settings.SourcePath, todayFolder, settings.LastDailyBackupDate);
                settings.LastDailyBackupDate = now;
            }

            // 4. ДИНАМИЧЕСКАЯ ОЧИСТКА (удаляем 8-й день)
            var dailyFolders = Directory.GetDirectories(dailyRootDir)
                                        .Select(d => new DirectoryInfo(d))
                                        .OrderByDescending(d => d.CreationTime)
                                        .ToList();

            if (dailyFolders.Count > 7)
            {
                foreach (var oldFolder in dailyFolders.Skip(7))
                {
                    logger($"Удаляем старый бэкап: {oldFolder.Name}");
                    Directory.Delete(oldFolder.FullName, true);
                }
            }

            logger("Бэкап успешно завершен!");
        }

        private static void CopyModifiedFiles(string sourceDir, string destDir, DateTime lastBackup)
        {
            foreach (string file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                FileInfo fi = new FileInfo(file);
                if (fi.LastWriteTime > lastBackup)
                {
                    // Создаем структуру подпапок в целевой папке
                    string relativePath = file.Substring(sourceDir.Length + 1);
                    string targetFile = Path.Combine(destDir, relativePath);

                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                    File.Copy(file, targetFile, true);
                }
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir, bool recursive)
        {
            var dir = new DirectoryInfo(sourceDir);
            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestDir = Path.Combine(destDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestDir, true);
                }
            }
        }

        private static void ClearDirectory(string path)
        {
            DirectoryInfo di = new DirectoryInfo(path);
            foreach (FileInfo file in di.GetFiles()) file.Delete();
            foreach (DirectoryInfo dir in di.GetDirectories()) dir.Delete(true);
        }
    }
}
