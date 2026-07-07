    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Runtime.InteropServices;

namespace AutoSaver
    {
        public static class BackupEngine
        {

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        // BackupEngine.ExecuteBackupAsync (исправленный)
        public static async Task ExecuteBackupAsync(BackupSettings settings, IProgress<BackupProgressData> progress, Action<string> logger)
        {

            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);

            try
            {
                // Проверка путей
                if (string.IsNullOrEmpty(settings.SourcePath) || string.IsNullOrEmpty(settings.DestinationPath))
                {
                    logger?.Invoke("Ошибка: Пути не настроены!");
                    return;
                }
                if (!Directory.Exists(settings.SourcePath))
                {
                    logger?.Invoke($"Ошибка: Исходная папка не существует: {settings.SourcePath}");
                    return;
                }

                // Создание папок назначения
                string monthlyDir = Path.Combine(settings.DestinationPath, "Month_Full");
                string dailyRootDir = Path.Combine(settings.DestinationPath, "Daily_Changes");
                Directory.CreateDirectory(monthlyDir);
                Directory.CreateDirectory(dailyRootDir);

                DateTime now = DateTime.Now;

                // ========== 1. ПОЛНЫЙ БЭКАП (раз в месяц) ==========
                bool needFull = settings.LastFullBackupDate.Month != now.Month || !Directory.EnumerateFileSystemEntries(monthlyDir).Any();
                if (needFull)
                {
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Обновление полной копии месяца...");
                    ClearDirectory(monthlyDir);

                    // Получаем все файлы с информацией
                    var allFiles = Directory.GetFiles(settings.SourcePath, "*.*", SearchOption.AllDirectories)
                                            .Select(f => new FileInfo(f))
                                            .ToList();

                    long totalSize = allFiles.Sum(f => f.Length);
                    long copiedSize = 0;
                    var sw = Stopwatch.StartNew();
                    int fileCount = allFiles.Count;

                    for (int i = 0; i < fileCount; i++)
                    {
                        var fi = allFiles[i];
                        try
                        {
                            // Используем более надежный способ получения относительного пути
                            string relativePath = Path.GetRelativePath(settings.SourcePath, fi.FullName);
                            string destFile = Path.Combine(monthlyDir, relativePath);

                            Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? string.Empty);

                            // Само копирование
                            await Task.Run(() => fi.CopyTo(destFile, true));

                            copiedSize += fi.Length;
                            // ... (твой код с progress?.Report) ...
                            logger?.Invoke($"Сохранен: {relativePath}");
                        }
                        catch (Exception ex)
                        {
                            logger?.Invoke($"Ошибка файла {fi.Name}: {ex.Message}");
                            // Продолжаем цикл, не вылетаем!
                            continue;
                        }
                    }
                    sw.Stop();
                    settings.LastFullBackupDate = now;
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Месячный бэкап завершён.");
                }

                // ========== 2. ЕЖЕДНЕВНЫЙ ИНКРЕМЕНТ ==========
                string todayFolder = Path.Combine(dailyRootDir, now.ToString("yyyy-MM-dd"));
                if (!Directory.Exists(todayFolder))
                {
                    // Ищем изменённые файлы после последнего инкремента
                    var changedFiles = Directory.GetFiles(settings.SourcePath, "*.*", SearchOption.AllDirectories)
                                                .Select(f => new FileInfo(f))
                                                .Where(fi => fi.LastWriteTime > settings.LastDailyBackupDate)
                                                .ToList();

                    if (changedFiles.Any())
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Копирование изменений за сегодня...");
                        long totalSize = changedFiles.Sum(f => f.Length);
                        long copiedSize = 0;
                        var sw = Stopwatch.StartNew();
                        int fileCount = changedFiles.Count;

                        for (int i = 0; i < fileCount; i++)
                        {
                            var fi = changedFiles[i];
                            string relativePath = fi.FullName.Substring(settings.SourcePath.Length).TrimStart('\\', '/');
                            string destFile = Path.Combine(todayFolder, relativePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? string.Empty);

                            await Task.Run(() => fi.CopyTo(destFile, true));

                            copiedSize += fi.Length;

                            double percent = (double)copiedSize / totalSize * 100;
                            double speed = sw.Elapsed.TotalSeconds > 0 ? (copiedSize / 1024.0 / 1024.0) / sw.Elapsed.TotalSeconds : 0;

                            progress?.Report(new BackupProgressData
                            {
                                Percentage = percent,
                                Speed = speed,
                                TimeElapsed = sw.Elapsed.ToString(@"hh\:mm\:ss"),
                                CurrentFileNumber = i + 1,
                                TotalFiles = fileCount,
                                CopiedBytes = copiedSize,
                                TotalBytes = totalSize
                            });

                            logger?.Invoke($"Сохранен (инкремент): {relativePath}");
                        }
                        sw.Stop();
                        settings.LastDailyBackupDate = now;
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Дневной инкремент завершён.");
                    }
                    else
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Изменений не найдено. Создана пустая папка дня.");
                        Directory.CreateDirectory(todayFolder);
                    }
                }

                // ========== 3. ОЧИСТКА СТАРЫХ ИНКРЕМЕНТОВ (оставляем 7 последних) ==========
                var dailyFolders = Directory.GetDirectories(dailyRootDir)
                                            .Select(d => new DirectoryInfo(d))
                                            .OrderByDescending(d => d.CreationTime)
                                            .ToList();
                if (dailyFolders.Count > 7)
                {
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Удаление старых бэкапов (оставляем 7)...");
                    foreach (var oldFolder in dailyFolders.Skip(7))
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Удаляем: {oldFolder.Name}");
                        try
                        {
                            await Task.Run(() => Directory.Delete(oldFolder.FullName, true));
                        }
                        catch (Exception ex)
                        {
                            logger?.Invoke($"Ошибка удаления {oldFolder.Name}: {ex.Message}");
                        }
                    }
                }

                logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Бэкап успешно завершён!");
            }
            finally
            {
                // Разрешаем системе снова засыпать, когда всё готово
                SetThreadExecutionState(ES_CONTINUOUS);
            }

            
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
