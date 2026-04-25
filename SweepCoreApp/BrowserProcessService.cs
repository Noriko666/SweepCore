using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SweepCoreApp
{
    internal sealed class BrowserProcessService
    {
        private static readonly Dictionary<string, string> SupportedBrowsers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome", "Google Chrome" },
            { "msedge", "Microsoft Edge" },
            { "brave", "Brave" },
            { "firefox", "Mozilla Firefox" }
        };

        public BrowserCloseResult CloseBrowsers(IEnumerable<string> processNames)
        {
            var result = new BrowserCloseResult();
            var requested = processNames == null
                ? new List<string>()
                : processNames
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var closedLabels = new List<string>();
            var failedLabels = new List<string>();

            foreach (var processName in requested)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    continue;
                }

                foreach (var process in processes)
                {
                    result.AttemptedProcessCount++;
                    bool closed = false;

                    try
                    {
                        if (process.HasExited)
                        {
                            result.ClosedProcessCount++;
                            closed = true;
                        }
                        else
                        {
                            if (process.MainWindowHandle != IntPtr.Zero)
                            {
                                try
                                {
                                    process.CloseMainWindow();
                                }
                                catch
                                {
                                }

                                try
                                {
                                    process.WaitForExit(10000);
                                }
                                catch
                                {
                                }
                            }

                            if (process.HasExited)
                            {
                                result.ClosedProcessCount++;
                                closed = true;
                            }
                        }
                    }
                    catch
                    {
                        closed = false;
                    }
                    finally
                    {
                        process.Dispose();
                    }

                    string label = SupportedBrowsers.ContainsKey(processName)
                        ? SupportedBrowsers[processName]
                        : processName;

                    if (closed)
                    {
                        closedLabels.Add(label);
                    }
                    else
                    {
                        result.FailedProcessCount++;
                        failedLabels.Add(label);
                    }
                }
            }

            closedLabels = closedLabels
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
            failedLabels = failedLabels
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (result.AttemptedProcessCount == 0)
            {
                result.Summary = "No supported browser processes were running.";
            }
            else if (result.FailedProcessCount == 0)
            {
                result.Summary = closedLabels.Count == 0
                    ? "Browser processes were already closed."
                    : "Closed browser processes: " + string.Join(", ", closedLabels);
            }
            else
            {
                result.Summary = "Could not close gracefully: " + string.Join(", ", failedLabels);
            }

            return result;
        }

        public static List<string> GetRequiredBrowserProcesses(IEnumerable<ScanEntry> entries)
        {
            var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
            {
                return processNames.ToList();
            }

            foreach (var entry in entries)
            {
                if (entry == null ||
                    (!string.Equals(entry.Section, "Browser Cache", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(entry.Section, "Browser Data", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string category = entry.Category ?? string.Empty;

                if (category.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase))
                {
                    processNames.Add("chrome");
                }
                else if (category.StartsWith("Edge", StringComparison.OrdinalIgnoreCase))
                {
                    processNames.Add("msedge");
                }
                else if (category.StartsWith("Brave", StringComparison.OrdinalIgnoreCase))
                {
                    processNames.Add("brave");
                }
                else if (category.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase))
                {
                    processNames.Add("firefox");
                }
            }

            return processNames.ToList();
        }

        public static string BuildBrowserLabelSummary(IEnumerable<string> processNames)
        {
            var labels = new List<string>();

            if (processNames != null)
            {
                foreach (var processName in processNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (SupportedBrowsers.ContainsKey(processName))
                    {
                        labels.Add(SupportedBrowsers[processName]);
                    }
                }
            }

            return string.Join(", ", labels.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        }
    }
}

