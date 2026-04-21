using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace SweepCoreApp
{
    internal sealed class RecycleBinDeletionService
    {
        public DeletionBatchResult MoveToRecycleBin(
            IEnumerable<ScanEntry> entries,
            IProgress<OperationProgressInfo> progress = null)
        {
            var result = new DeletionBatchResult();
            var entryList = entries == null
                ? new List<ScanEntry>()
                : entries.Where(item => item != null).ToList();

            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, "sweepcore-actions-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            result.LogPath = logPath;

            using (var writer = new StreamWriter(logPath, true, Encoding.UTF8))
            {
                writer.AutoFlush = true;
                writer.WriteLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " Cleanup Batch ===");
                ReportProgress(progress, "Starting cleanup...", 0, entryList.Count);

                foreach (var entry in entryList.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
                {
                    result.RequestedCount++;
                    result.RequestedBytes += entry.SizeBytes;
                    string progressMessage;

                    if (entry.Risk != ScanRisk.Safe)
                    {
                        result.SkippedCount++;
                        writer.WriteLine("SKIPPED | Non-safe item | " + entry.Path);
                        progressMessage = "Skipped blocked item";
                    }
                    else if (string.IsNullOrWhiteSpace(entry.Path) || !File.Exists(entry.Path))
                    {
                        result.SkippedCount++;
                        writer.WriteLine("SKIPPED | Missing file | " + entry.Path);
                        progressMessage = "Skipped missing file";
                    }
                    else if (IsFileLocked(entry.Path))
                    {
                        result.SkippedCount++;
                        writer.WriteLine("SKIPPED | File is in use | " + entry.Path);
                        progressMessage = "Skipped open file";
                    }
                    else
                    {
                        try
                        {
                            FileSystem.DeleteFile(
                                entry.Path,
                                UIOption.OnlyErrorDialogs,
                                RecycleOption.SendToRecycleBin,
                                UICancelOption.DoNothing);

                            result.MovedCount++;
                            result.MovedBytes += entry.SizeBytes;
                            writer.WriteLine("MOVED   | " + entry.Path);
                            progressMessage = "Moved item to Recycle Bin";
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            writer.WriteLine("FAILED  | " + entry.Path + " | " + ex.Message);
                            progressMessage = "Failed to move item";
                        }
                    }

                    ReportProgress(progress, progressMessage, result.RequestedCount, entryList.Count);
                }

                writer.WriteLine(
                    string.Format(
                        "SUMMARY | Requested={0} Moved={1} Skipped={2} Failed={3} RequestedBytes={4} MovedBytes={5}",
                        result.RequestedCount,
                        result.MovedCount,
                        result.SkippedCount,
                        result.FailedCount,
                        result.RequestedBytes,
                        result.MovedBytes));
                writer.WriteLine();
            }

            return result;
        }

        private static void ReportProgress(IProgress<OperationProgressInfo> progress, string message, int current, int total)
        {
            if (progress == null)
            {
                return;
            }

            progress.Report(new OperationProgressInfo
            {
                Message = message,
                Current = current,
                Total = total,
                IsIndeterminate = false
            });
        }

        private static bool IsFileLocked(string path)
        {
            FileStream stream = null;

            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }
        }
    }
}

