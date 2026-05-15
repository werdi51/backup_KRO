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
                // ---------- ШАПКА СЕССИИ ----------
                logger?.Invoke("==================================================");
                logger?.Invoke($"СЕССИЯ БЭКАПА ОТ {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                logger?.Invoke("==================================================");

                var sessionSw = Stopwatch.StartNew();
                long totalBytesCopied = 0;
                int totalFilesCopied = 0;

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

                string monthlyDir = Path.Combine(settings.DestinationPath, "Month_Full");
                string dailyRootDir = Path.Combine(settings.DestinationPath, "Daily_Changes");
                Directory.CreateDirectory(monthlyDir);
                Directory.CreateDirectory(dailyRootDir);

                DateTime now = DateTime.Now;

                // ========== 1. ПОЛНЫЙ БЭКАП (раз в месяц) ==========
                bool needFull = settings.LastFullBackupDate.Month != now.Month ||
                                !Directory.EnumerateFileSystemEntries(monthlyDir).Any();
                if (needFull)
                {
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Обновление полной копии месяца...");
                    ClearDirectory(monthlyDir);

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
                        if (fi.Name.StartsWith("~$") || fi.Attributes.HasFlag(FileAttributes.Hidden))
                            continue;

                        try
                        {
                            string relativePath = Path.GetRelativePath(settings.SourcePath, fi.FullName);
                            string destFile = Path.Combine(monthlyDir, relativePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? string.Empty);

                            bool copied = false;
                            for (int attempt = 0; attempt < 3; attempt++)
                            {
                                try
                                {
                                    fi.CopyTo(destFile, true);
                                    copied = true;
                                    break;
                                }
                                catch (IOException) when (attempt < 2)
                                {
                                    await Task.Delay(5000);
                                }
                            }
                            if (!copied)
                            {
                                logger?.Invoke($"[ПРОПУЩЕНО]: Файл {fi.Name} занят, не удалось скопировать после 3 попыток.");
                                continue;
                            }

                            copiedSize += fi.Length;
                            totalBytesCopied += fi.Length;
                            totalFilesCopied++;

                            double percent = (double)copiedSize / totalSize * 100;
                            double speed = sw.Elapsed.TotalSeconds > 0
                                ? (copiedSize / 1024.0 / 1024.0) / sw.Elapsed.TotalSeconds
                                : 0;

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

                            // Краткое логирование (раскомментируйте, если хотите видеть каждый файл)
                            logger?.Invoke($"[OK] {relativePath}");
                        }
                        catch (Exception ex)
                        {
                            logger?.Invoke($"Ошибка файла {fi.Name}: {ex.Message}");
                            continue;
                        }
                    }
                    sw.Stop();
                    settings.LastFullBackupDate = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                    settings.LastDailyBackupDate = settings.LastFullBackupDate;
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Месячный бэкап завершён.");
                }
                else
                {
                    // ========== 2. ЕЖЕДНЕВНЫЙ ИНКРЕМЕНТ ==========
                    string todayFolder = Path.Combine(dailyRootDir, now.ToString("yyyy-MM-dd"));

                    // Ищем файлы, изменённые с момента последней успешной проверки
                    var changedFiles = Directory.GetFiles(settings.SourcePath, "*.*", SearchOption.AllDirectories)
                                                .Select(f => new FileInfo(f))
                                                .Where(fi => fi.LastWriteTime > settings.LastDailyBackupDate.AddSeconds(-1))
                                                .ToList();

                    if (changedFiles.Any())
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Копирование изменений за сегодня...");
                        long totalSize = changedFiles.Sum(f => f.Length);
                        long copiedSize = 0;
                        var sw = Stopwatch.StartNew();
                        int fileCount = changedFiles.Count;
                        bool hasError = false;

                        // Папка дня создастся, если её ещё нет (при повторных запусках докладываем файлы туда же)
                        Directory.CreateDirectory(todayFolder);

                        for (int i = 0; i < fileCount; i++)
                        {
                            var fi = changedFiles[i];
                            if (fi.Name.StartsWith("~$") || fi.Attributes.HasFlag(FileAttributes.Hidden))
                                continue;

                            try
                            {
                                string relativePath = Path.GetRelativePath(settings.SourcePath, fi.FullName);
                                string destFile = Path.Combine(todayFolder, relativePath);
                                Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? string.Empty);

                                bool copied = false;
                                for (int attempt = 0; attempt < 3; attempt++)
                                {
                                    try
                                    {
                                        fi.CopyTo(destFile, true);
                                        copied = true;
                                        break;
                                    }
                                    catch (IOException) when (attempt < 2)
                                    {
                                        await Task.Delay(5000);
                                    }
                                }
                                if (!copied)
                                {
                                    logger?.Invoke($"[ПРОПУЩЕНО инкремент]: {fi.Name} занят после 3 попыток");
                                    hasError = true;
                                    continue;
                                }

                                copiedSize += fi.Length;
                                totalBytesCopied += fi.Length;
                                totalFilesCopied++;

                                double percent = (double)copiedSize / totalSize * 100;
                                double speed = sw.Elapsed.TotalSeconds > 0
                                    ? (copiedSize / 1024.0 / 1024.0) / sw.Elapsed.TotalSeconds
                                    : 0;

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

                                // Краткое логирование для инкремента
                                logger?.Invoke($"[OK инкремент] {relativePath}");
                            }
                            catch (Exception ex)
                            {
                                hasError = true;
                                logger?.Invoke($"Ошибка (инкремент) {fi.Name}: {ex.Message}");
                                continue;
                            }
                        }
                        sw.Stop();

                        if (hasError)
                        {
                            logger?.Invoke($"Обнаружены ошибки – папка инкремента будет удалена, чтобы повторить позже.");
                            try { Directory.Delete(todayFolder, true); } catch { }
                        }
                        else
                        {
                            settings.LastDailyBackupDate = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                            logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Дневной инкремент завершён.");
                        }
                    }
                    else
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Изменений не найдено.");
                        // Обновляем дату в любом случае, чтобы при следующем запуске не анализировать те же файлы
                        settings.LastDailyBackupDate = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                    }
                }
     

                // ========== 3. ОЧИСТКА СТАРЫХ ИНКРЕМЕНТОВ ==========
                var dailyFolders = Directory.GetDirectories(dailyRootDir)
                                            .Select(d => new DirectoryInfo(d))
                                            .OrderByDescending(d => d.Name)
                                            .ToList();
                if (dailyFolders.Count > 7)
                {
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Удаление старых бэкапов (оставляем 7)...");
                    foreach (var oldFolder in dailyFolders.Skip(7))
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Удаляем: {oldFolder.Name}");
                        try
                        {
                            foreach (var file in Directory.GetFiles(oldFolder.FullName, "*", SearchOption.AllDirectories))
                                File.SetAttributes(file, FileAttributes.Normal);
                            Directory.Delete(oldFolder.FullName, true);
                        }
                        catch (Exception ex)
                        {
                            logger?.Invoke($"Ошибка удаления {oldFolder.Name}: {ex.Message}");
                        }
                    }
                }

                // ---------- ПОДВАЛ СЕССИИ И СТАТИСТИКА ----------
                sessionSw.Stop();
                logger?.Invoke("--------------------------------------------------");
                logger?.Invoke($"ИТОГО ЗА СЕССИЮ:");
                logger?.Invoke($"Время выполнения: {sessionSw.Elapsed:hh\\:mm\\:ss}");
                logger?.Invoke($"Файлов скопировано: {totalFilesCopied}");
                logger?.Invoke($"Общий объем: {FormatSize(totalBytesCopied)}");
                logger?.Invoke("==================================================\n");
                logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Бэкап успешно завершён!");
            }
            finally
            {
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

        private static string FormatSize(long bytes)
        {
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            if (gb >= 1) return $"{gb:F2} ГБ";
            double mb = bytes / 1024.0 / 1024.0;
            return $"{mb:F2} МБ";
        }


    }
    }
