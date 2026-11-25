    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Runtime.InteropServices;
    using System.Threading;

namespace AutoSaver
    {
        public static class BackupEngine
        {
        //P/Invoke
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_CONTINUOUS = 0x80000000; 
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        public static async Task ExecuteBackupAsync(BackupSettings settings, IProgress<BackupProgressData> progress, Action<string> logger)
        {
            using var mutex = new Mutex(true, @"Global\AutoSaver_BackupLock", out bool isNew);
            if (!isNew)
            {
                logger?.Invoke("Бэкап уже выполняется другим экземпляром.");
                return;
            }

            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);

            try
            {
                logger?.Invoke($"СЕССИЯ БЭКАПА ОТ {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                logger?.Invoke("__________________________________________________");

                var sessionSw = Stopwatch.StartNew();
                long totalBytesCopied = 0;
                int totalFilesCopied = 0;
                var failedFiles = new List<string>();

                // Проверка путей
                if (string.IsNullOrEmpty(settings.SourcePath) || string.IsNullOrEmpty(settings.DestinationPath))
                {
                    logger?.Invoke("Ошибка: Пути не настроены");
                    return;
                }
                if (!Directory.Exists(settings.SourcePath))
                {
                    logger?.Invoke($"Ошибка: Исходная папка не существует: {settings.SourcePath}");

                    return;
                }

                string monthlyDir = Path.Combine(settings.DestinationPath, "Month_Full");
                string monthlyNewDir = Path.Combine(settings.DestinationPath, "Month_Full_new");
                string monthlyOldDir = Path.Combine(settings.DestinationPath, "Month_Full_old");


                string dailyRootDir = Path.Combine(settings.DestinationPath, "Daily_Changes");
                Directory.CreateDirectory(monthlyDir);
                Directory.CreateDirectory(dailyRootDir);

                DateTime now = DateTime.Now;

                // Если выбрано 31, а в месяце 30 дней (или февраль), берём последний день месяца
                int day = Math.Clamp(settings.FullBackupDay, 1, DateTime.DaysInMonth(now.Year, now.Month));
                DateTime scheduledFull = new DateTime(now.Year, now.Month, day);

                bool needFull = !Directory.EnumerateFileSystemEntries(monthlyDir).Any() ||
                                (now.Date >= scheduledFull && settings.LastFullBackupDate < scheduledFull);

                if (needFull)
                {
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Обновление полной копии месяца...");

                    var allFiles = new List<FileInfo>();
                    long totalSize = 0;

                    //Фил
                    foreach (string filePath in SafeEnumerateFiles(settings.SourcePath))
                    {
                        var fi = new FileInfo(filePath);
                        try
                        {
                            // Проверяем существование
                            if (!fi.Exists || fi.Name.StartsWith("~$") || fi.Attributes.HasFlag(FileAttributes.Hidden))
                                continue;

                            totalSize += fi.Length;
                            allFiles.Add(fi);
                        }
                        catch
                        {
                        }
                    }

                    long copiedSize = 0;
                    var sw = Stopwatch.StartNew();
                    int fileCount = allFiles.Count;

                    for (int i = 0; i < fileCount; i++)
                    {
                        var fi = allFiles[i]; 


                        try
                        {
                            string relativePath = Path.GetRelativePath(settings.SourcePath, fi.FullName); //получение относительног пути
                            string destFile = Path.Combine(monthlyDir, relativePath); //обьединение папки месячных копий и относительного пути
                            Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? string.Empty);

                            bool copied = IsSameFile(fi, destFile);   // не изменился: пропускаем копирование
                            for (int attempt = 0; !copied && attempt < 3; attempt++)
                            {
                                try
                                {
                                    CopyFileShared(fi.FullName, destFile);
                                    copied = true;
                                    break;
                                }
                                catch (Exception) when (attempt < 2)  
                                {
                                    await Task.Delay(5000);
                                }
                            }
                            if (!copied)
                            {
                                logger?.Invoke($"[ПРОПУЩЕНО]: Файл {fi.Name} занят, не удалось скопировать после 3 попыток");
                                failedFiles.Add(fi.Name);  

                                continue;
                            }

                            copiedSize += fi.Length;
                            totalBytesCopied += fi.Length;
                            totalFilesCopied++;

                            double percent = totalSize > 0
                                ? (double)copiedSize / totalSize * 100
                                : (i == fileCount - 1 ? 100 : 0);

                            double elapsedSec = sw.Elapsed.TotalSeconds;
                            double speed = elapsedSec > 0 && copiedSize > 0
                                ? (copiedSize / 1024.0 / 1024.0) / elapsedSec
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

                            logger?.Invoke($"[OK] {relativePath}");
                        }
                        catch (Exception ex)
                        {
                            logger?.Invoke($"Ошибка файла {fi.Name}: {ex.Message}");
                            failedFiles.Add(fi.Name);  
                            
                            continue;
                        }
                    }
                    sw.Stop();
                    if (failedFiles.Count == 0)
                    {
                        settings.LastFullBackupDate = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                        settings.LastDailyBackupDate = settings.LastFullBackupDate;
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Месячный бэкап завершён");
                    }
                    else
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Месячный бэкап неполный ({failedFiles.Count} файлов занято), повтор при следующем запуске");
                    }
                }
                else
                {
                    // Каждодневное копирование
                    string todayFolder = Path.Combine(dailyRootDir, now.ToString("yyyy-MM-dd"));

                    // Ищем файлы изменённые с момента последней успешной проверки
                    var changedFiles = new List<FileInfo>();
                    long totalSize = 0;

                    foreach (string filePath in SafeEnumerateFiles(settings.SourcePath))
                    {
                        var fi = new FileInfo(filePath);
                        try
                        {
                            if (!fi.Exists || fi.Name.StartsWith("~$") || fi.Attributes.HasFlag(FileAttributes.Hidden))
                                continue;

                            // Добавляем новыец
                            if (fi.LastWriteTime > settings.LastDailyBackupDate.AddSeconds(-1))
                            {
                                totalSize += fi.Length;
                                changedFiles.Add(fi);
                            }
                        }
                        catch
                        {
                        }
                    }

                    if (changedFiles.Any())
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Копирование изменений за сегодня...");
                        long copiedSize = 0;
                        var sw = Stopwatch.StartNew();
                        int fileCount = changedFiles.Count;
                        bool hasError = false;

                        Directory.CreateDirectory(todayFolder);
                        for (int i = 0; i < fileCount; i++)
                        {
                            var fi = changedFiles[i];


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
                                        CopyFileShared(fi.FullName, destFile);
                                        copied = true;
                                        break;
                                    }
                                    catch (Exception) when (attempt < 2)
                                    {
                                        await Task.Delay(5000);
                                    }
                                }
                                if (!copied)
                                {
                                    logger?.Invoke($"[ПРОПУЩЕНО дневное]: {fi.Name} занят после 3 попыток");
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

                                logger?.Invoke($"[OK инкремент] {relativePath}");
                            }
                            catch (Exception ex)
                            {
                                hasError = true;
                                logger?.Invoke($"Ошибка (инкремент) {fi.Name}: {ex.Message}");
                                failedFiles.Add(fi.Name);  
                                continue;
                            }
                        }
                        sw.Stop();
                        if (hasError)
                        {
                            logger?.Invoke("Обнаружены ошибки – папка копии будет удалена, чтобы повторить позже.");
                            try
                            {
                                foreach (var f in Directory.EnumerateFiles(todayFolder, "*", SearchOption.AllDirectories))
                                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                                Directory.Delete(todayFolder, true);
                            }
                            catch { }
                        }
                        else
                        {
                            settings.LastDailyBackupDate = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                            logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Дневная копия завершена.");
                        }
                    }
                    else
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Изменений не найдено.");
                        settings.LastDailyBackupDate = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));

                    }
                }
     

                //Очистка старых бэкапов
                var dailyFolders = Directory.GetDirectories(dailyRootDir)
                                            .Select(d => new DirectoryInfo(d))
                                            .OrderByDescending(d => d.Name)
                                            .ToList();
                int keepDays = Math.Max(1, settings.DailyRetentionDays);
                if (dailyFolders.Count > keepDays)
                {
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Удаление старых бэкапов (хранится {keepDays} дн.)");
                    foreach (var oldFolder in dailyFolders.Skip(keepDays))
                    {
                        logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Удаляем: {oldFolder.Name}");
                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(oldFolder.FullName, "*", SearchOption.AllDirectories))
                                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }

                            Directory.Delete(oldFolder.FullName, true);
                        }
                        catch (Exception ex)
                        {
                            logger?.Invoke($"Ошибка удаления {oldFolder.Name}: {ex.Message}");
                        }
                    }
                }

                if (failedFiles.Count > 0)
                {
                    logger?.Invoke("--------------------------------------------------");
                    logger?.Invoke($" НЕ УДАЛОСЬ СКОПИРОВАТЬ ({failedFiles.Count} ФАЙЛОВ):");
                    foreach (var fileName in failedFiles)
                        logger?.Invoke($" - {fileName}");
                }


                //СТАТИСТИКА
                sessionSw.Stop();
                logger?.Invoke("--------------------------------------------------");
                logger?.Invoke($"ИТОГО ЗА СЕССИЮ:");
                logger?.Invoke($"Время выполнения: {sessionSw.Elapsed:hh\\:mm\\:ss}");
                logger?.Invoke($"Файлов скопировано: {totalFilesCopied}");
                logger?.Invoke($"Общий объем: {FormatSize(totalBytesCopied)}");
                logger?.Invoke("==================================================\n");
                if (failedFiles.Count > 0)
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Бэкап завершён с {failedFiles.Count} ошибками");
                else
                    logger?.Invoke($"[{DateTime.Now:HH:mm:ss}] Бэкап успешно завершён");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"КРИТИЧЕСКАЯ ОШИБКА В ДВИЖКЕ: {ex.Message}");
                logger?.Invoke($"Стек: {ex.StackTrace}");
            }
            finally
            {
                SetThreadExecutionState(ES_CONTINUOUS);
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

        private static string FormatSize(long bytes)
        {
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            if (gb >= 1) return $"{gb:F2} ГБ";
            double mb = bytes / 1024.0 / 1024.0;
            return $"{mb:F2} МБ";
        }


        private static IEnumerable<string> SafeEnumerateFiles(string root)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            };
            return Directory.EnumerateFiles(root, "*", options);
        }

        private static void CopyFileShared(string source, string dest)
        {
            using (var src = new FileStream(source, FileMode.Open, FileAccess.Read,
                                            FileShare.ReadWrite | FileShare.Delete))
            {
                if (File.Exists(dest))
                    File.SetAttributes(dest, FileAttributes.Normal);

                using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                    src.CopyTo(dst, 1024 * 1024);
            }
            File.SetLastWriteTime(dest, File.GetLastWriteTime(source));
        }

        private static bool IsSameFile(FileInfo src, string dest)
        {
            var d = new FileInfo(dest);
            return d.Exists
                && d.Length == src.Length
                && Math.Abs((d.LastWriteTime - src.LastWriteTime).TotalSeconds) < 2;
        }

    }



    }
