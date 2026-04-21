using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;

namespace SweepCoreApp
{
    internal sealed class InstalledAppsService
    {
        private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public List<InstalledAppInfo> Load()
        {
            var apps = new List<InstalledAppInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            LoadFromHive(apps, seen, RegistryHive.LocalMachine, RegistryView.Registry64, "Machine");
            LoadFromHive(apps, seen, RegistryHive.LocalMachine, RegistryView.Registry32, "Machine");
            LoadFromHive(apps, seen, RegistryHive.CurrentUser, RegistryView.Registry64, "User");
            LoadFromHive(apps, seen, RegistryHive.CurrentUser, RegistryView.Registry32, "User");

            return apps
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void LoadFromHive(
            List<InstalledAppInfo> apps,
            HashSet<string> seen,
            RegistryHive hive,
            RegistryView view,
            string scope)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var uninstallKey = baseKey.OpenSubKey(UninstallRegistryPath))
                {
                    if (uninstallKey == null)
                    {
                        return;
                    }

                    foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        using (var appKey = uninstallKey.OpenSubKey(subKeyName))
                        {
                            if (appKey == null || ShouldSkip(appKey))
                            {
                                continue;
                            }

                            string name = ReadString(appKey, "DisplayName");
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                continue;
                            }

                            string version = ReadString(appKey, "DisplayVersion");
                            string publisher = ReadString(appKey, "Publisher");
                            string uninstallCommand = ReadString(appKey, "UninstallString");
                            string quietUninstallCommand = ReadString(appKey, "QuietUninstallString");
                            string displayIconPath = ReadString(appKey, "DisplayIcon");
                            long estimatedSizeBytes = ReadEstimatedSizeBytes(appKey);
                            DateTime? installDate = ReadInstallDate(appKey);
                            string key = (name ?? string.Empty) + "|" + (version ?? string.Empty) + "|" + (publisher ?? string.Empty) + "|" + scope;

                            if (!seen.Add(key))
                            {
                                continue;
                            }

                            apps.Add(new InstalledAppInfo
                            {
                                Name = name,
                                Version = string.IsNullOrWhiteSpace(version) ? "-" : version,
                                Publisher = string.IsNullOrWhiteSpace(publisher) ? "-" : publisher,
                                Scope = scope,
                                UninstallCommand = string.IsNullOrWhiteSpace(uninstallCommand) ? quietUninstallCommand : uninstallCommand,
                                DisplayIconPath = displayIconPath,
                                EstimatedSizeBytes = estimatedSizeBytes,
                                InstallDate = installDate
                            });
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool ShouldSkip(RegistryKey appKey)
        {
            if (ReadDword(appKey, "SystemComponent") == 1)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(ReadString(appKey, "ParentKeyName")))
            {
                return true;
            }

            string releaseType = ReadString(appKey, "ReleaseType");
            if (!string.IsNullOrWhiteSpace(releaseType))
            {
                if (releaseType.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    releaseType.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    releaseType.IndexOf("Security", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ReadDword(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name);
                if (value == null)
                {
                    return 0;
                }

                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static long ReadEstimatedSizeBytes(RegistryKey key)
        {
            try
            {
                object value = key.GetValue("EstimatedSize");
                if (value == null)
                {
                    return 0;
                }

                long sizeInKb = Convert.ToInt64(value);
                return sizeInKb <= 0 ? 0 : sizeInKb * 1024;
            }
            catch
            {
                return 0;
            }
        }

        private static DateTime? ReadInstallDate(RegistryKey key)
        {
            try
            {
                string raw = ReadString(key, "InstallDate");
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                DateTime parsed;
                if (DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    return parsed;
                }

                if (DateTime.TryParse(raw, out parsed))
                {
                    return parsed;
                }

                return null;
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

