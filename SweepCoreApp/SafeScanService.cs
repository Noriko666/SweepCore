using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SweepCoreApp
{
    internal sealed class SafeScanService
    {
        private const string TemporaryFilesSection = "Temporary Files";
        private const string BrowserDataSection = "Browser Data";

        public ScanResult Run(IProgress<OperationProgressInfo> progress = null)
        {
            return Run(new[] { BrowserDataType.Cache }, false, progress);
        }

        public ScanResult Run(
            IEnumerable<BrowserDataType> browserDataTypes,
            bool includeRecentTempFiles,
            IProgress<OperationProgressInfo> progress = null)
        {
            var entries = new List<ScanEntry>();
            var rules = BuildRules(browserDataTypes, includeRecentTempFiles);
            var cutoffUtc = DateTime.UtcNow;
            int ruleIndex = 0;

            ReportProgress(progress, "Preparing scan...", true, 0, 0);

            foreach (var rule in rules)
            {
                ruleIndex++;
                int inspectedCount = 0;
                ReportProgress(progress, "Scanning " + rule.Category + "...", true, 0, 0);

                if (string.IsNullOrWhiteSpace(rule.RootPath) || !Directory.Exists(rule.RootPath))
                {
                    continue;
                }

                foreach (var file in EnumerateFilesSafe(rule.RootPath, rule.SearchPattern, rule.Recursive))
                {
                    inspectedCount++;
                    if (inspectedCount == 1 || inspectedCount % 250 == 0)
                    {
                        ReportProgress(
                            progress,
                            "Scanning " + rule.Category + "... " + inspectedCount + " files checked",
                            true,
                            0,
                            0);
                    }

                    FileInfo info;
                    try
                    {
                        info = new FileInfo(file);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!info.Exists)
                    {
                        continue;
                    }

                    if (rule.MinSizeBytes > 0 && info.Length < rule.MinSizeBytes)
                    {
                        continue;
                    }

                    if (rule.MinAgeDays > 0 && info.LastWriteTimeUtc > cutoffUtc.AddDays(-rule.MinAgeDays))
                    {
                        continue;
                    }

                    if (IsProtected(file, rule.AllowProtectedExtensions))
                    {
                        entries.Add(BuildProtectedEntry(file, rule.Section, rule.Category));
                        continue;
                    }

                    entries.Add(new ScanEntry
                    {
                        Section = rule.Section,
                        Category = rule.Category,
                        Path = info.FullName,
                        SizeBytes = info.Length,
                        LastWriteTime = info.LastWriteTime,
                        Reason = rule.Reason,
                        Risk = rule.Risk
                    });
                }
            }

            ReportProgress(progress, "Finalizing scan results...", true, 0, 0);

            entries = entries
                .OrderByDescending(item => item.SizeBytes)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var summary = new ScanSummary
            {
                TotalCount = entries.Count,
                SafeCount = entries.Count(item => item.Risk == ScanRisk.Safe),
                ReviewCount = entries.Count(item => item.Risk == ScanRisk.Review),
                ProtectedCount = entries.Count(item => item.Risk == ScanRisk.Protected),
                TotalBytes = entries.Sum(item => item.SizeBytes)
            };

            ReportProgress(progress, "Scan complete", false, 1, 1);
            return new ScanResult(entries, summary);
        }

        private static List<CandidateRule> BuildRules(
            IEnumerable<BrowserDataType> browserDataTypes,
            bool includeRecentTempFiles)
        {
            var rules = new List<CandidateRule>();
            var selectedBrowserDataTypes = BuildBrowserDataTypeSet(browserDataTypes);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string windowsTemp = Path.Combine(windowsDirectory, "Temp");
            string userTemp = Path.GetTempPath();
            string crashDumps = Path.Combine(localAppData, "CrashDumps");
            string windowsErrorReports = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft",
                "Windows",
                "WER");

            rules.Add(new CandidateRule
            {
                Section = TemporaryFilesSection,
                Category = "User Temp",
                RootPath = userTemp,
                SearchPattern = "*",
                Recursive = true,
                MinAgeDays = includeRecentTempFiles ? 0 : 7,
                MinSizeBytes = 1024,
                Risk = ScanRisk.Safe,
                Reason = includeRecentTempFiles
                    ? "Temporary files from the user temp folder."
                    : "Temporary files from the user temp folder that are older than one week."
            });

            rules.Add(new CandidateRule
            {
                Section = TemporaryFilesSection,
                Category = "Windows Temp",
                RootPath = windowsTemp,
                SearchPattern = "*",
                Recursive = true,
                MinAgeDays = includeRecentTempFiles ? 0 : 7,
                MinSizeBytes = 1024,
                Risk = ScanRisk.Safe,
                Reason = includeRecentTempFiles
                    ? "Temporary files from the Windows temp folder."
                    : "Temporary files from the Windows temp folder that are older than one week."
            });

            rules.Add(new CandidateRule
            {
                Section = TemporaryFilesSection,
                Category = "Crash Dumps",
                RootPath = crashDumps,
                SearchPattern = "*.dmp",
                Recursive = true,
                MinAgeDays = 7,
                MinSizeBytes = 1024,
                Risk = ScanRisk.Safe,
                Reason = "Crash dump files that Windows can recreate if needed."
            });

            rules.Add(new CandidateRule
            {
                Section = TemporaryFilesSection,
                Category = "Windows Error Reports",
                RootPath = windowsErrorReports,
                SearchPattern = "*.wer",
                Recursive = true,
                MinAgeDays = 7,
                MinSizeBytes = 512,
                Risk = ScanRisk.Safe,
                Reason = "Temporary Windows error reports."
            });

            AddChromiumRules(
                rules,
                Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                "Chrome",
                selectedBrowserDataTypes);
            AddChromiumRules(
                rules,
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
                "Edge",
                selectedBrowserDataTypes);
            AddChromiumRules(
                rules,
                Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
                "Brave",
                selectedBrowserDataTypes);
            AddFirefoxRules(
                rules,
                Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"),
                selectedBrowserDataTypes);

            return rules;
        }

        private static HashSet<BrowserDataType> BuildBrowserDataTypeSet(IEnumerable<BrowserDataType> browserDataTypes)
        {
            return browserDataTypes == null
                ? new HashSet<BrowserDataType>(new[] { BrowserDataType.Cache })
                : new HashSet<BrowserDataType>(browserDataTypes);
        }

        private static void AddChromiumRules(
            List<CandidateRule> rules,
            string userDataRoot,
            string browserName,
            HashSet<BrowserDataType> selectedBrowserDataTypes)
        {
            if (!Directory.Exists(userDataRoot))
            {
                return;
            }

            foreach (string profileDirectory in EnumerateDirectoriesSafe(userDataRoot))
            {
                string profileName = Path.GetFileName(profileDirectory);
                if (!IsChromiumProfileDirectory(profileName))
                {
                    continue;
                }

                if (selectedBrowserDataTypes.Contains(BrowserDataType.Cache))
                {
                    AddCacheRule(rules, browserName, profileName, Path.Combine(profileDirectory, "Cache", "Cache_Data"));
                    AddCacheRule(rules, browserName, profileName, Path.Combine(profileDirectory, "Code Cache"));
                    AddCacheRule(rules, browserName, profileName, Path.Combine(profileDirectory, "GPUCache"));
                    AddCacheRule(rules, browserName, profileName, Path.Combine(profileDirectory, "DawnCache"));
                }

                if (selectedBrowserDataTypes.Contains(BrowserDataType.Cookies))
                {
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "Cookies",
                        "Cookies",
                        "Cookies keep website login sessions and site preferences. Removing them signs you out of many websites.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        Path.Combine(profileDirectory, "Network"),
                        "Cookies",
                        "Cookies",
                        "Cookies keep website login sessions and site preferences. Removing them signs you out of many websites.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "Cookies-journal",
                        "Cookies",
                        "Cookie database journal file.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "Cookies-wal",
                        "Cookies",
                        "Cookie database write-ahead log.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "Cookies-shm",
                        "Cookies",
                        "Cookie database shared-memory file.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        Path.Combine(profileDirectory, "Network"),
                        "Cookies-journal",
                        "Cookies",
                        "Cookie database journal file.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        Path.Combine(profileDirectory, "Network"),
                        "Cookies-wal",
                        "Cookies",
                        "Cookie database write-ahead log.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        Path.Combine(profileDirectory, "Network"),
                        "Cookies-shm",
                        "Cookies",
                        "Cookie database shared-memory file.");
                }

                if (selectedBrowserDataTypes.Contains(BrowserDataType.History))
                {
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "History",
                        "History",
                        "Browsing history database. Saved passwords are stored separately and are not included.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "History-journal",
                        "History",
                        "Browsing history database journal file.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "History-wal",
                        "History",
                        "Browsing history database write-ahead log.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "History-shm",
                        "History",
                        "Browsing history database shared-memory file.");
                    AddProfileFileRule(
                        rules,
                        browserName,
                        profileName,
                        profileDirectory,
                        "Visited Links",
                        "History",
                        "Visited-link state used by the browser.");
                }
            }
        }

        private static void AddFirefoxRules(
            List<CandidateRule> rules,
            string profilesRoot,
            HashSet<BrowserDataType> selectedBrowserDataTypes)
        {
            if (!Directory.Exists(profilesRoot))
            {
                return;
            }

            foreach (string profileDirectory in EnumerateDirectoriesSafe(profilesRoot))
            {
                string profileName = Path.GetFileName(profileDirectory);
                if (selectedBrowserDataTypes.Contains(BrowserDataType.Cache))
                {
                    AddFirefoxCacheRule(rules, profileName, Path.Combine(profileDirectory, "cache2", "entries"));
                    AddFirefoxCacheRule(rules, profileName, Path.Combine(profileDirectory, "startupCache"));
                }

                if (selectedBrowserDataTypes.Contains(BrowserDataType.Cookies))
                {
                    AddProfileFileRule(
                        rules,
                        "Firefox",
                        profileName,
                        profileDirectory,
                        "cookies.sqlite",
                        "Cookies",
                        "Cookies keep website login sessions and site preferences. Removing them signs you out of many websites.",
                        true);
                    AddProfileFileRule(
                        rules,
                        "Firefox",
                        profileName,
                        profileDirectory,
                        "cookies.sqlite-wal",
                        "Cookies",
                        "Cookie database write-ahead log.",
                        true);
                    AddProfileFileRule(
                        rules,
                        "Firefox",
                        profileName,
                        profileDirectory,
                        "cookies.sqlite-shm",
                        "Cookies",
                        "Cookie database shared-memory file.",
                        true);
                }

                if (selectedBrowserDataTypes.Contains(BrowserDataType.History))
                {
                    AddFirefoxHistoryProtectedRule(rules, profileName, profileDirectory);
                }
            }
        }

        private static void AddCacheRule(List<CandidateRule> rules, string browserName, string profileName, string cacheRoot)
        {
            if (!Directory.Exists(cacheRoot))
            {
                return;
            }

            rules.Add(new CandidateRule
            {
                Section = BrowserDataSection,
                Category = browserName + " Cache (" + profileName + ")",
                RootPath = cacheRoot,
                SearchPattern = "*",
                Recursive = true,
                MinAgeDays = 1,
                MinSizeBytes = 512,
                Risk = ScanRisk.Safe,
                Reason = "Only browser cache folders are included. Saved passwords, addresses, and form data are excluded."
            });
        }

        private static void AddFirefoxCacheRule(List<CandidateRule> rules, string profileName, string cacheRoot)
        {
            if (!Directory.Exists(cacheRoot))
            {
                return;
            }

            rules.Add(new CandidateRule
            {
                Section = BrowserDataSection,
                Category = "Firefox Cache (" + profileName + ")",
                RootPath = cacheRoot,
                SearchPattern = "*",
                Recursive = true,
                MinAgeDays = 1,
                MinSizeBytes = 512,
                Risk = ScanRisk.Safe,
                Reason = "Only Firefox cache folders are included. Saved passwords, addresses, and form data are excluded."
            });
        }

        private static void AddProfileFileRule(
            List<CandidateRule> rules,
            string browserName,
            string profileName,
            string rootPath,
            string fileName,
            string dataTypeLabel,
            string reason,
            bool allowProtectedExtensions = false)
        {
            if (!Directory.Exists(rootPath))
            {
                return;
            }

            string filePath = Path.Combine(rootPath, fileName);
            if (!File.Exists(filePath))
            {
                return;
            }

            rules.Add(new CandidateRule
            {
                Section = BrowserDataSection,
                Category = browserName + " " + dataTypeLabel + " (" + profileName + ")",
                RootPath = rootPath,
                SearchPattern = fileName,
                Recursive = false,
                MinAgeDays = 0,
                MinSizeBytes = 1,
                Risk = ScanRisk.Safe,
                Reason = reason,
                AllowProtectedExtensions = allowProtectedExtensions
            });
        }

        private static void AddFirefoxHistoryProtectedRule(List<CandidateRule> rules, string profileName, string profileDirectory)
        {
            string historyDatabase = Path.Combine(profileDirectory, "places.sqlite");
            if (!File.Exists(historyDatabase))
            {
                return;
            }

            rules.Add(new CandidateRule
            {
                Section = BrowserDataSection,
                Category = "Firefox History (" + profileName + ")",
                RootPath = profileDirectory,
                SearchPattern = "places.sqlite",
                Recursive = false,
                MinAgeDays = 0,
                MinSizeBytes = 1,
                Risk = ScanRisk.Protected,
                Reason = "Firefox stores browsing history together with bookmarks in places.sqlite, so SweepCore does not remove it.",
                AllowProtectedExtensions = true
            });
        }

        private static bool IsChromiumProfileDirectory(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return false;
            }

            return profileName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                   profileName.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) ||
                   profileName.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateFilesSafe(string rootPath, string searchPattern, bool recursive)
        {
            string normalizedRoot;
            try
            {
                normalizedRoot = NormalizeFullPath(rootPath);
            }
            catch
            {
                yield break;
            }

            if (IsReparsePoint(normalizedRoot))
            {
                yield break;
            }

            var pending = new Queue<string>();
            pending.Enqueue(normalizedRoot);

            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                IEnumerable<string> files = Array.Empty<string>();
                IEnumerable<string> directories = Array.Empty<string>();

                try
                {
                    files = Directory.EnumerateFiles(current, searchPattern, SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                foreach (var file in files)
                {
                    string fullPath;
                    try
                    {
                        fullPath = NormalizeFullPath(file);
                    }
                    catch
                    {
                        continue;
                    }

                    if (IsReparsePoint(fullPath) || !IsSameOrUnderRoot(fullPath, normalizedRoot))
                    {
                        continue;
                    }

                    yield return fullPath;
                }

                if (!recursive)
                {
                    continue;
                }

                try
                {
                    directories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                foreach (var directory in directories)
                {
                    string fullPath;
                    try
                    {
                        fullPath = NormalizeFullPath(directory);
                    }
                    catch
                    {
                        continue;
                    }

                    if (IsReparsePoint(fullPath) || !IsSameOrUnderRoot(fullPath, normalizedRoot))
                    {
                        continue;
                    }

                    pending.Enqueue(fullPath);
                }
            }
        }

        private static IEnumerable<string> EnumerateDirectoriesSafe(string rootPath)
        {
            try
            {
                return Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(item => !IsReparsePoint(item))
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static bool IsProtected(string path, bool allowProtectedExtensions)
        {
            string fullPath;
            try
            {
                fullPath = NormalizeFullPath(path);
            }
            catch
            {
                return true;
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string downloads = Path.Combine(userProfile, "Downloads");
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");

            string[] protectedRoots =
            {
                documents,
                desktop,
                downloads,
                oneDrive,
                Path.Combine(userProfile, "Pictures"),
                Path.Combine(userProfile, "Videos"),
                Path.Combine(userProfile, "Music"),
                Path.Combine(userProfile, "source"),
                Path.Combine(userProfile, ".git")
            };

            foreach (string root in protectedRoots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                if (IsSameOrUnderRoot(fullPath, root))
                {
                    return true;
                }
            }

            if (allowProtectedExtensions)
            {
                return false;
            }

            string extension = Path.GetExtension(fullPath);
            string[] protectedExtensions =
            {
                ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pst", ".ost",
                ".kdbx", ".sql", ".sqlite", ".db", ".accdb", ".vcxproj", ".csproj",
                ".sln", ".zip", ".7z", ".rar", ".jpg", ".png", ".raw", ".psd", ".blend",
                ".vmx", ".vmdk", ".vhd", ".vhdx"
            };

            return protectedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsSameOrUnderRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            string normalizedPath;
            string normalizedRoot;
            try
            {
                normalizedPath = NormalizeFullPath(path);
                normalizedRoot = NormalizeFullPath(root);
            }
            catch
            {
                return false;
            }

            if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string rootWithSeparator = EnsureTrailingDirectorySeparator(normalizedRoot);
            return normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFullPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath);

            while (fullPath.Length > root.Length &&
                   (fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                    fullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)))
            {
                fullPath = fullPath.Substring(0, fullPath.Length - 1);
            }

            return fullPath;
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return true;
            }
        }

        private static ScanEntry BuildProtectedEntry(string path, string section, string category)
        {
            var info = new FileInfo(path);

            return new ScanEntry
            {
                Section = section,
                Category = category,
                Path = path,
                SizeBytes = info.Exists ? info.Length : 0,
                LastWriteTime = info.Exists ? info.LastWriteTime : DateTime.MinValue,
                Reason = "Blocked by protection rules and excluded from cleanup.",
                Risk = ScanRisk.Protected
            };
        }

        private static void ReportProgress(
            IProgress<OperationProgressInfo> progress,
            string message,
            bool isIndeterminate,
            int current,
            int total)
        {
            if (progress == null)
            {
                return;
            }

            progress.Report(new OperationProgressInfo
            {
                Message = message,
                IsIndeterminate = isIndeterminate,
                Current = current,
                Total = total
            });
        }
    }
}

