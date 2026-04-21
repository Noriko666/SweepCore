using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SweepCoreApp
{
    internal sealed class SafeScanService
    {
        private const string TemporaryFilesSection = "Temporary Files";
        private const string BrowserCacheSection = "Browser Cache";

        public ScanResult Run(IProgress<OperationProgressInfo> progress = null)
        {
            var entries = new List<ScanEntry>();
            var rules = BuildRules();
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

                    if (IsProtected(file))
                    {
                        entries.Add(BuildProtectedEntry(file, rule.Section, rule.Category));
                        continue;
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

        private static List<CandidateRule> BuildRules()
        {
            var rules = new List<CandidateRule>();
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
                MinAgeDays = 2,
                MinSizeBytes = 1024,
                Risk = ScanRisk.Safe,
                Reason = "Temporary files from the user temp folder."
            });

            rules.Add(new CandidateRule
            {
                Section = TemporaryFilesSection,
                Category = "Windows Temp",
                RootPath = windowsTemp,
                SearchPattern = "*",
                Recursive = true,
                MinAgeDays = 2,
                MinSizeBytes = 1024,
                Risk = ScanRisk.Safe,
                Reason = "Temporary files from the Windows temp folder."
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

            AddChromiumCacheRules(
                rules,
                Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                "Chrome");
            AddChromiumCacheRules(
                rules,
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
                "Edge");
            AddChromiumCacheRules(
                rules,
                Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
                "Brave");
            AddFirefoxCacheRules(
                rules,
                Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"));

            return rules;
        }

        private static void AddChromiumCacheRules(List<CandidateRule> rules, string userDataRoot, string browserName)
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

                AddCacheRule(rules, browserName, profileName, Path.Combine(profileDirectory, "Cache", "Cache_Data"));
                AddCacheRule(rules, browserName, profileName, Path.Combine(profileDirectory, "Code Cache"));
                AddCacheRule(rules, browserName, profileName, Path.Combine(profileDirectory, "GPUCache"));
                AddCacheRule(rules, browserName, profileName, Path.Combine(profileDirectory, "DawnCache"));
            }
        }

        private static void AddFirefoxCacheRules(List<CandidateRule> rules, string profilesRoot)
        {
            if (!Directory.Exists(profilesRoot))
            {
                return;
            }

            foreach (string profileDirectory in EnumerateDirectoriesSafe(profilesRoot))
            {
                string profileName = Path.GetFileName(profileDirectory);
                AddFirefoxCacheRule(rules, profileName, Path.Combine(profileDirectory, "cache2", "entries"));
                AddFirefoxCacheRule(rules, profileName, Path.Combine(profileDirectory, "startupCache"));
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
                Section = BrowserCacheSection,
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
                Section = BrowserCacheSection,
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
            var pending = new Queue<string>();
            pending.Enqueue(rootPath);

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
                    yield return file;
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
                    pending.Enqueue(directory);
                }
            }
        }

        private static IEnumerable<string> EnumerateDirectoriesSafe(string rootPath)
        {
            try
            {
                return Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static bool IsProtected(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
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

                if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
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

