using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace SweepCoreApp
{
    internal sealed class StartupProgramsService
    {
        private const string RunRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string StartupApprovedFolderPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
        private const string AppModelSystemAppDataPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

        public List<StartupItemInfo> Load()
        {
            var items = new List<StartupItemInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packagedAppNames = LoadPackagedAppNames();

            LoadRunItems(items, seen, RegistryHive.CurrentUser, RegistryView.Registry64, "User");
            LoadRunItems(items, seen, RegistryHive.LocalMachine, RegistryView.Registry64, "Machine");
            LoadRunItems(items, seen, RegistryHive.LocalMachine, RegistryView.Registry32, "Machine");

            LoadStartupFolderItems(items, seen, RegistryHive.CurrentUser, RegistryView.Registry64, "User", Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            LoadStartupFolderItems(items, seen, RegistryHive.LocalMachine, RegistryView.Registry64, "Machine", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
            LoadAppModelStartupTasks(items, seen, packagedAppNames);

            return items
                .OrderByDescending(item => item.IsEnabled)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void SetEnabled(StartupItemInfo item, bool isEnabled)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            if (string.IsNullOrWhiteSpace(item.ToggleRegistryPath))
            {
                throw new InvalidOperationException("This startup entry cannot be updated.");
            }

            using (var baseKey = RegistryKey.OpenBaseKey(item.Hive, item.View))
            {
                if (item.ToggleUsesStateDword)
                {
                    using (var itemKey = baseKey.CreateSubKey(item.ToggleRegistryPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (itemKey == null)
                        {
                            throw new InvalidOperationException("Could not open the startup key.");
                        }

                        itemKey.SetValue(item.ToggleRegistryValueName ?? "State", isEnabled ? 2 : 1, RegistryValueKind.DWord);
                        itemKey.SetValue("UserEnabledStartupOnce", 1, RegistryValueKind.DWord);
                        if (!isEnabled)
                        {
                            itemKey.SetValue("LastDisabledTime", (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), RegistryValueKind.DWord);
                        }
                    }
                }
                else
                {
                    using (var approvedKey = baseKey.CreateSubKey(item.ToggleRegistryPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (approvedKey == null)
                        {
                            throw new InvalidOperationException("Could not open StartupApproved.");
                        }

                        var existing = ReadBinary(approvedKey, item.ToggleRegistryValueName);
                        approvedKey.SetValue(
                            item.ToggleRegistryValueName,
                            BuildStartupApprovedData(isEnabled, existing),
                            RegistryValueKind.Binary);
                    }
                }
            }
        }

        private static void LoadRunItems(
            List<StartupItemInfo> items,
            HashSet<string> seen,
            RegistryHive hive,
            RegistryView view,
            string scope)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var runKey = baseKey.OpenSubKey(RunRegistryPath))
                using (var approvedKey = baseKey.OpenSubKey(StartupApprovedRunPath))
                {
                    if (runKey == null)
                    {
                        return;
                    }

                    foreach (string valueName in runKey.GetValueNames())
                    {
                        string command = ReadString(runKey, valueName);
                        if (string.IsNullOrWhiteSpace(valueName) || string.IsNullOrWhiteSpace(command))
                        {
                            continue;
                        }

                        string dedupeKey = "run|" + scope + "|" + valueName + "|" + command;
                        if (!seen.Add(dedupeKey))
                        {
                            continue;
                        }

                        items.Add(new StartupItemInfo
                        {
                            Name = valueName,
                            Command = command,
                            Scope = scope,
                            SourceKind = "Registrierung",
                            Location = string.Format("{0} Run ({1})", GetHiveLabel(hive), view == RegistryView.Registry32 ? "32-bit" : "64-bit"),
                            IsEnabled = ReadStartupApprovedState(approvedKey, valueName),
                            Hive = hive,
                            View = view,
                            ToggleRegistryPath = StartupApprovedRunPath,
                            ToggleRegistryValueName = valueName,
                            ToggleUsesStateDword = false
                        });
                    }
                }
            }
            catch
            {
            }
        }

        private static void LoadStartupFolderItems(
            List<StartupItemInfo> items,
            HashSet<string> seen,
            RegistryHive hive,
            RegistryView view,
            string scope,
            string folderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    return;
                }

                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var approvedKey = baseKey.OpenSubKey(StartupApprovedFolderPath))
                {
                    foreach (string filePath in Directory.GetFiles(folderPath))
                    {
                        string fileName = Path.GetFileName(filePath);
                        if (string.IsNullOrWhiteSpace(fileName) || string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string dedupeKey = "folder|" + scope + "|" + fileName + "|" + filePath;
                        if (!seen.Add(dedupeKey))
                        {
                            continue;
                        }

                        items.Add(new StartupItemInfo
                        {
                            Name = Path.GetFileNameWithoutExtension(fileName),
                            Command = filePath,
                            Scope = scope,
                            SourceKind = "Startup folder",
                            Location = folderPath,
                            IsEnabled = ReadStartupApprovedState(approvedKey, fileName),
                            Hive = hive,
                            View = view,
                            ToggleRegistryPath = StartupApprovedFolderPath,
                            ToggleRegistryValueName = fileName,
                            ToggleUsesStateDword = false
                        });
                    }
                }
            }
            catch
            {
            }
        }

        private static void LoadAppModelStartupTasks(
            List<StartupItemInfo> items,
            HashSet<string> seen,
            Dictionary<string, string> packagedAppNames)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var systemAppDataKey = baseKey.OpenSubKey(AppModelSystemAppDataPath))
                {
                    if (systemAppDataKey == null)
                    {
                        return;
                    }

                    foreach (string packageFamilyName in systemAppDataKey.GetSubKeyNames())
                    {
                        using (var packageKey = systemAppDataKey.OpenSubKey(packageFamilyName))
                        {
                            if (packageKey == null)
                            {
                                continue;
                            }

                            foreach (string taskId in packageKey.GetSubKeyNames())
                            {
                                using (var taskKey = packageKey.OpenSubKey(taskId))
                                {
                                    if (taskKey == null || !HasStateValue(taskKey))
                                    {
                                        continue;
                                    }

                                    string dedupeKey = "appx|" + packageFamilyName + "|" + taskId;
                                    if (!seen.Add(dedupeKey))
                                    {
                                        continue;
                                    }

                                    items.Add(new StartupItemInfo
                                    {
                                        Name = ResolvePackagedAppName(packageFamilyName, taskId, packagedAppNames),
                                        Command = "Paket: " + packageFamilyName,
                                        Scope = "User",
                                        SourceKind = "App startup task",
                                        Location = "Task: " + taskId,
                                        IsEnabled = ReadAppModelStartupState(taskKey),
                                        Hive = RegistryHive.CurrentUser,
                                        View = RegistryView.Registry64,
                                        ToggleRegistryPath = AppModelSystemAppDataPath + "\\" + packageFamilyName + "\\" + taskId,
                                        ToggleRegistryValueName = "State",
                                        ToggleUsesStateDword = true
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool ReadStartupApprovedState(RegistryKey approvedKey, string valueName)
        {
            byte[] data = ReadBinary(approvedKey, valueName);
            if (data == null || data.Length == 0)
            {
                return true;
            }

            return data[0] != 0x03;
        }

        private static bool ReadAppModelStartupState(RegistryKey key)
        {
            try
            {
                object value = key.GetValue("State");
                if (value == null)
                {
                    return false;
                }

                return Convert.ToInt32(value) == 2;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasStateValue(RegistryKey key)
        {
            try
            {
                return key.GetValueNames().Any(name => string.Equals(name, "State", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static byte[] BuildStartupApprovedData(bool isEnabled, byte[] existing)
        {
            int length = existing != null && existing.Length >= 12 ? existing.Length : 12;
            var data = new byte[length];

            if (existing != null && existing.Length > 0)
            {
                Buffer.BlockCopy(existing, 0, data, 0, Math.Min(existing.Length, data.Length));
            }

            data[0] = isEnabled ? (byte)0x02 : (byte)0x03;

            if (data.Length >= 12)
            {
                byte[] timestamp = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
                Buffer.BlockCopy(timestamp, 0, data, 4, 8);
            }

            return data;
        }

        private static byte[] ReadBinary(RegistryKey key, string name)
        {
            try
            {
                object value = key == null ? null : key.GetValue(name);
                return value as byte[];
            }
            catch
            {
                return null;
            }
        }

        private static string ReadString(RegistryKey key, string name)
        {
            try
            {
                object value = key == null ? null : key.GetValue(name);
                return value == null ? string.Empty : value.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Dictionary<string, string> LoadPackagedAppNames()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-StartApps | ForEach-Object { Write-Output ($_.Name + [char]9 + $_.AppID) }\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return map;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts = rawLine.Split(new[] { '\t' }, 2);
                        if (parts.Length != 2)
                        {
                            continue;
                        }

                        string displayName = parts[0].Trim();
                        string appId = parts[1].Trim();
                        int separator = appId.IndexOf('!');
                        if (separator <= 0 || string.IsNullOrWhiteSpace(displayName))
                        {
                            continue;
                        }

                        string packageFamilyName = appId.Substring(0, separator);
                        if (!map.ContainsKey(packageFamilyName) || ShouldReplacePackagedName(map[packageFamilyName], displayName))
                        {
                            map[packageFamilyName] = displayName;
                        }
                    }
                }
            }
            catch
            {
            }

            return map;
        }

        private static bool ShouldReplacePackagedName(string currentValue, string candidate)
        {
            if (string.IsNullOrWhiteSpace(currentValue))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            return candidate.Length < currentValue.Length;
        }

        private static string ResolvePackagedAppName(string packageFamilyName, string taskId, Dictionary<string, string> packagedAppNames)
        {
            string mappedName;
            if (packagedAppNames != null && packagedAppNames.TryGetValue(packageFamilyName, out mappedName) && !string.IsNullOrWhiteSpace(mappedName))
            {
                return mappedName;
            }

            if (string.Equals(packageFamilyName, "MicrosoftWindows.CrossDevice_cw5n1h2txyewy", StringComparison.OrdinalIgnoreCase))
            {
                return "Mobile devices";
            }

            string shortName = packageFamilyName;
            int separator = shortName.IndexOf('_');
            if (separator > 0)
            {
                shortName = shortName.Substring(0, separator);
            }

            if (shortName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
            {
                shortName = shortName.Substring("Microsoft.".Length);
            }

            if (shortName.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase))
            {
                shortName = shortName.Substring("MicrosoftWindows.".Length);
            }

            shortName = shortName.Replace('.', ' ');

            var buffer = new List<char>();
            for (int i = 0; i < shortName.Length; i++)
            {
                char current = shortName[i];
                if (i > 0 && char.IsUpper(current) && shortName[i - 1] != ' ' && !char.IsUpper(shortName[i - 1]))
                {
                    buffer.Add(' ');
                }
                buffer.Add(current);
            }

            string candidate = new string(buffer.ToArray()).Trim();
            return string.IsNullOrWhiteSpace(candidate) ? taskId : candidate;
        }

        private static string GetHiveLabel(RegistryHive hive)
        {
            return hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
        }
    }
}


