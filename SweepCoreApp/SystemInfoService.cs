using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.Devices;
using Microsoft.Win32;

namespace SweepCoreApp
{
    internal sealed class SystemInfoService
    {
        public SystemSnapshot Build(IList<InstalledAppInfo> apps, IList<ScanEntry> entries)
        {
            apps = apps ?? new List<InstalledAppInfo>();
            entries = entries ?? new List<ScanEntry>();

            var snapshot = new SystemSnapshot
            {
                DeviceName = Environment.MachineName,
                OperatingSystem = ReadOperatingSystemLabel(),
                Processor = ReadProcessorName(),
                MemoryDisplay = ReadMemoryDisplay(),
                InstalledAppCount = apps.Count,
                MachineAppCount = apps.Count(item => string.Equals(item.Scope, "Machine", StringComparison.OrdinalIgnoreCase)),
                UserAppCount = apps.Count(item => string.Equals(item.Scope, "User", StringComparison.OrdinalIgnoreCase)),
                TempFileCount = entries.Count(item => string.Equals(item.Section, "Temporary Files", StringComparison.OrdinalIgnoreCase)),
                TempFileBytes = entries
                    .Where(item => string.Equals(item.Section, "Temporary Files", StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.SizeBytes),
                BrowserCacheCount = entries.Count(item => IsBrowserDataSection(item.Section)),
                BrowserCacheBytes = entries
                    .Where(item => IsBrowserDataSection(item.Section))
                    .Sum(item => item.SizeBytes),
                CleanableCount = entries.Count(item => item.IsCleanable),
                CleanableBytes = entries.Where(item => item.IsCleanable).Sum(item => item.SizeBytes),
                BlockedCount = entries.Count(item => !item.IsCleanable)
            };

            PopulateDriveInfo(snapshot);
            return snapshot;
        }

        private static bool IsBrowserDataSection(string section)
        {
            return string.Equals(section, "Browser Cache", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(section, "Browser Data", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadOperatingSystemLabel()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null)
                    {
                        return Environment.OSVersion.VersionString;
                    }

                    string productName = ReadString(key, "ProductName");
                    string displayVersion = ReadString(key, "DisplayVersion");
                    if (!string.IsNullOrWhiteSpace(productName) && !string.IsNullOrWhiteSpace(displayVersion))
                    {
                        return productName + " " + displayVersion;
                    }

                    return string.IsNullOrWhiteSpace(productName)
                        ? Environment.OSVersion.VersionString
                        : productName;
                }
            }
            catch
            {
                return Environment.OSVersion.VersionString;
            }
        }

        private static string ReadProcessorName()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    string name = key == null ? string.Empty : ReadString(key, "ProcessorNameString");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
            catch
            {
            }

            string fallback = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            return string.IsNullOrWhiteSpace(fallback) ? "Unknown CPU" : fallback;
        }

        private static string ReadMemoryDisplay()
        {
            try
            {
                var computerInfo = new ComputerInfo();
                return SizeFormatter.Format((long)computerInfo.TotalPhysicalMemory) + " RAM";
            }
            catch
            {
                return "RAM unavailable";
            }
        }

        private static void PopulateDriveInfo(SystemSnapshot snapshot)
        {
            try
            {
                string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
                var drive = new DriveInfo(systemRoot);
                if (!drive.IsReady)
                {
                    snapshot.SystemDriveLabel = "System drive unavailable";
                    snapshot.SystemDriveUsage = "Drive information is not ready.";
                    snapshot.SystemDriveUsagePercent = 0;
                    return;
                }

                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;
                long used = total - free;

                snapshot.SystemDriveLabel = "System Drive " + drive.Name;
                snapshot.SystemDriveUsage = SizeFormatter.Format(used) + " used  |  " + SizeFormatter.Format(free) + " free of " + SizeFormatter.Format(total);
                snapshot.SystemDriveUsagePercent = total == 0 ? 0 : (used * 100.0) / total;
            }
            catch
            {
                snapshot.SystemDriveLabel = "System drive unavailable";
                snapshot.SystemDriveUsage = "Drive information could not be read.";
                snapshot.SystemDriveUsagePercent = 0;
            }
        }

        private static string ReadString(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name);
                return value == null ? string.Empty : value.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

