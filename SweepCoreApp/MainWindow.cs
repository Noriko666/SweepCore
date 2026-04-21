using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SweepCoreApp
{
    internal sealed class MainWindow : Window
    {
        private enum NavigationSection
        {
            Overview,
            Cleanup,
            Apps,
            Startup
        }

        private const string TargetTemp = "temp";
        private const string TargetChrome = "chrome";
        private const string TargetEdge = "edge";
        private const string TargetBrave = "brave";
        private const string TargetFirefox = "firefox";

        private static readonly string[] CleanupTargetOrder =
        {
            TargetTemp,
            TargetChrome,
            TargetEdge,
            TargetBrave,
            TargetFirefox
        };

        private readonly SafeScanService scanService;
        private readonly RecycleBinDeletionService deletionService;
        private readonly BrowserProcessService browserProcessService;
        private readonly InstalledAppsService installedAppsService;
        private readonly StartupProgramsService startupProgramsService;
        private readonly SystemInfoService systemInfoService;
        private readonly ObservableCollection<InstalledAppInfo> visibleApps;
        private readonly HashSet<string> selectedCleanupTargets;
        private readonly Dictionary<NavigationSection, Button> navigationButtons;

        private ContentControl pageHost;
        private TextBlock headerEyebrowText;
        private TextBlock headerDeviceText;
        private TextBlock headerMetaText;
        private TextBlock headerScanStatusText;
        private TextBlock headerRightLabelText;
        private TextBlock headerSelectionStatusText;
        private TextBlock headerLastActionText;
        private Grid headerButtonGrid;
        private TextBlock statusText;
        private Border operationProgressBorder;
        private TextBlock operationText;
        private ProgressBar operationProgressBar;
        private Button headerScanButton;
        private Button headerCleanButton;

        private TextBox appSearchBox;
        private ComboBox appsFilterComboBox;
        private ComboBox appsSortComboBox;
        private WrapPanel appsTilePanel;
        private TextBlock appsSummaryText;
        private TextBox startupSearchBox;
        private ComboBox startupFilterComboBox;
        private StackPanel startupItemPanel;
        private TextBlock startupSummaryText;
        private WrapPanel cleanupTargetPanel;
        private TextBlock cleanerSummaryText;
        private TextBlock selectionModeText;
        private TextBlock selectionSummaryText;
        private TextBlock cleanupSelectionDetailsText;
        private TextBlock cleanupBreakdownTitleText;
        private TextBlock cleanupActionHintText;
        private StackPanel cleanupBreakdownPanel;
        private Button cleanupActionButton;
        private DataGrid cleanupPreviewGrid;
        private TextBlock cleanupPreviewCaptionText;

        private List<InstalledAppInfo> allInstalledApps;
        private List<StartupItemInfo> allStartupItems;
        private List<StartupItemInfo> visibleStartupItems;
        private List<ScanEntry> allEntries;
        private List<ScanEntry> currentEntries;
        private SystemSnapshot currentSnapshot;
        private string lastActionMessage;
        private string currentStatusMessage;
        private string appSearchQuery;
        private string startupSearchQuery;
        private string selectedAppsFilterKey;
        private string selectedStartupFilterKey;
        private string selectedAppsSortKey;
        private bool operationInProgress;
        private bool appsReloadInProgress;
        private bool startupReloadInProgress;
        private bool startupToggleInProgress;
        private bool hasScanRun;
        private bool isDarkMode;
        private int uninstallMonitorToken;
        private NavigationSection activeSection;
        private ScanResult lastScanResult;

        private sealed class CleanupTargetSnapshot
        {
            public string Key { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public int CleanableCount { get; set; }
            public long CleanableBytes { get; set; }
            public int BlockedCount { get; set; }
            public bool IsSelected { get; set; }
            public bool IsEnabled { get; set; }
        }

        private sealed class SelectionOption
        {
            public string Key { get; set; }
            public string Label { get; set; }

            public override string ToString()
            {
                return Label ?? string.Empty;
            }
        }

        public MainWindow()
        {
            scanService = new SafeScanService();
            deletionService = new RecycleBinDeletionService();
            browserProcessService = new BrowserProcessService();
            installedAppsService = new InstalledAppsService();
            startupProgramsService = new StartupProgramsService();
            systemInfoService = new SystemInfoService();
            visibleApps = new ObservableCollection<InstalledAppInfo>();
            selectedCleanupTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            navigationButtons = new Dictionary<NavigationSection, Button>();
            allInstalledApps = new List<InstalledAppInfo>();
            allStartupItems = new List<StartupItemInfo>();
            visibleStartupItems = new List<StartupItemInfo>();
            allEntries = new List<ScanEntry>();
            currentEntries = new List<ScanEntry>();
            appSearchQuery = string.Empty;
            startupSearchQuery = string.Empty;
            selectedAppsFilterKey = "all";
            selectedStartupFilterKey = "all";
            selectedAppsSortKey = "name";
            currentStatusMessage = "Loading workspace...";
            isDarkMode = true;
            activeSection = NavigationSection.Cleanup;

            Title = "SweepCore 1.0";
            Width = 1520;
            Height = 930;
            MinWidth = 1260;
            MinHeight = 760;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");
            Background = WindowBackgroundBrush();
            Content = BuildShell();

            Loaded += delegate { RefreshDashboard(); };
        }

        private UIElement BuildShell()
        {
            navigationButtons.Clear();
            ResetPageReferences();
            Background = WindowBackgroundBrush();

            var root = new Grid
            {
                Background = WindowBackgroundBrush()
            };
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(286)
            });
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            var sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);

            var mainDock = new DockPanel
            {
                Margin = new Thickness(24, 18, 24, 18),
                LastChildFill = true
            };
            Grid.SetColumn(mainDock, 1);
            root.Children.Add(mainDock);

            var statusBar = BuildStatusBar();
            DockPanel.SetDock(statusBar, Dock.Bottom);
            mainDock.Children.Add(statusBar);

            var header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            mainDock.Children.Add(header);

            pageHost = new ContentControl
            {
                Margin = new Thickness(0, 22, 0, 0)
            };
            mainDock.Children.Add(pageHost);

            RenderCurrentSection();
            UpdateHeaderState();
            UpdateSidebarState();
            UpdateStatusBarState();

            return root;
        }

        private UIElement BuildSidebar()
        {
            var shell = new Border
            {
                Margin = new Thickness(18),
                Padding = new Thickness(18),
                Background = SidebarBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(28)
            };

            var dock = new DockPanel
            {
                LastChildFill = true
            };
            shell.Child = dock;

            var content = new StackPanel();
            dock.Children.Add(content);

            content.Children.Add(BuildBrandCard());

            content.Children.Add(BuildNavigationButton("Clean up", NavigationSection.Cleanup));
            content.Children.Add(BuildNavigationButton("Uninstall apps", NavigationSection.Apps));
            content.Children.Add(BuildNavigationButton("Startup", NavigationSection.Startup));

            return shell;
        }

        private UIElement BuildBrandCard()
        {
            var card = CreateCardShell();
            card.Padding = new Thickness(18, 18, 18, 16);

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            card.Child = stack;

            var logo = BuildLogoVisual(58);
            var logoElement = logo as FrameworkElement;
            if (logoElement != null)
            {
                logoElement.HorizontalAlignment = HorizontalAlignment.Center;
            }
            stack.Children.Add(logo);

            stack.Children.Add(new TextBlock
            {
                Text = "SweepCore",
                FontFamily = HeadingFontFamily(),
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = PrimaryTextBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Simple mode",
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Foreground = AccentBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = "1. Scan  2. Select  3. Clean up",
                Margin = new Thickness(0, 14, 0, 0),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            return card;
        }

        private Button BuildNavigationButton(string label, NavigationSection section)
        {
            var button = new Button
            {
                Content = label,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(16, 14, 16, 14),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };

            var targetSection = section;
            button.Click += delegate { SwitchSection(targetSection); };
            navigationButtons[targetSection] = button;
            ApplyNavigationButtonStyle(button, targetSection == activeSection);

            return button;
        }

        private UIElement BuildHeader()
        {
            var shell = new Border
            {
                Background = HeroBackgroundBrush(),
                BorderBrush = HeroOutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(34),
                Padding = new Thickness(28, 24, 28, 24)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(360)
            });
            shell.Child = grid;

            var left = new StackPanel();
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            headerEyebrowText = new TextBlock
            {
                Text = "Simple flow",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = HeroSecondaryTextBrush()
            };
            left.Children.Add(headerEyebrowText);

            headerDeviceText = new TextBlock
            {
                Text = "Clean up",
                Margin = new Thickness(0, 8, 0, 0),
                FontFamily = HeadingFontFamily(),
                FontSize = 34,
                FontWeight = FontWeights.SemiBold,
                Foreground = HeroPrimaryTextBrush()
            };
            left.Children.Add(headerDeviceText);

            headerMetaText = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = HeroSecondaryTextBrush()
            };
            left.Children.Add(headerMetaText);

            headerScanStatusText = new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 0),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = HeroPrimaryTextBrush()
            };
            left.Children.Add(headerScanStatusText);

            var right = new Border
            {
                Background = HeroCardBrush(),
                BorderBrush = HeroOutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(20)
            };
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            var rightStack = new StackPanel();
            right.Child = rightStack;

            headerRightLabelText = new TextBlock
            {
                Text = "Next step",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = HeroSecondaryTextBrush()
            };
            rightStack.Children.Add(headerRightLabelText);

            headerSelectionStatusText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontFamily = HeadingFontFamily(),
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                Foreground = HeroPrimaryTextBrush()
            };
            rightStack.Children.Add(headerSelectionStatusText);

            headerLastActionText = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = HeroSecondaryTextBrush()
            };
            rightStack.Children.Add(headerLastActionText);

            headerButtonGrid = new Grid
            {
                Margin = new Thickness(0, 18, 0, 0)
            };
            headerButtonGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            headerButtonGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            rightStack.Children.Add(headerButtonGrid);

            headerScanButton = BuildPrimaryButton("1. Start scan", true);
            headerScanButton.Click += delegate { RefreshScanOnly(); };
            Grid.SetColumn(headerScanButton, 0);
            headerButtonGrid.Children.Add(headerScanButton);

            headerCleanButton = BuildSecondaryButton("2. Choose areas", true);
            headerCleanButton.Margin = new Thickness(10, 0, 0, 0);
            headerCleanButton.Click += delegate { HandleHeaderAction(); };
            Grid.SetColumn(headerCleanButton, 1);
            headerButtonGrid.Children.Add(headerCleanButton);

            return shell;
        }

        private Border BuildHeroChip(string text)
        {
            var chip = new Border
            {
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(12, 7, 12, 7),
                Background = HeroChipBrush(),
                CornerRadius = new CornerRadius(999)
            };
            chip.Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = HeroPrimaryTextBrush()
            };
            return chip;
        }

        private void RenderCurrentSection()
        {
            if (pageHost == null)
            {
                return;
            }

            ResetPageReferences();

            if (activeSection == NavigationSection.Apps)
            {
                pageHost.Content = BuildAppsSection();
                ApplyAppFilter();
            }
            else if (activeSection == NavigationSection.Startup)
            {
                pageHost.Content = BuildStartupSection();
                ApplyStartupFilter();
            }
            else
            {
                pageHost.Content = BuildCleanupSection();
                UpdateCleanupView();
            }

            UpdateNavigationState();
            UpdateHeaderState();
            UpdateSidebarState();
            UpdateStatusBarState();
        }

        private UIElement BuildOverviewSection()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            scroll.Content = root;

            root.Children.Add(BuildOverviewWelcomeCard());

            var metrics = new UniformGrid
            {
                Columns = 4,
                Margin = new Thickness(0, 20, 0, 0)
            };
            root.Children.Add(metrics);

            metrics.Children.Add(BuildMetricCard(
                currentSnapshot == null ? "--" : currentSnapshot.InstalledAppCount.ToString(),
                "Installed apps",
                "Loaded from Windows uninstall entries."));
            metrics.Children.Add(BuildMetricCard(
                currentSnapshot == null ? "--" : SizeFormatter.Format(currentSnapshot.CleanableBytes),
                "Ready after scan",
                hasScanRun ? string.Format("{0} cleanable item(s) found.", currentSnapshot.CleanableCount) : "Run a scan to populate this card."));
            metrics.Children.Add(BuildMetricCard(
                currentSnapshot == null ? "--" : SizeFormatter.Format(currentSnapshot.BrowserCacheBytes),
                "Browser cache",
                currentSnapshot == null ? "Waiting for results." : string.Format("{0} cache item(s) across supported browsers.", currentSnapshot.BrowserCacheCount)));
            metrics.Children.Add(BuildMetricCard(
                currentSnapshot == null ? "--" : string.Format("{0:0}%", currentSnapshot.SystemDriveUsagePercent),
                "System drive used",
                currentSnapshot == null ? "Drive info unavailable." : currentSnapshot.SystemDriveUsage));

            var lowerGrid = new Grid
            {
                Margin = new Thickness(0, 20, 0, 0)
            };
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.15, GridUnitType.Star)
            });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(0.85, GridUnitType.Star)
            });
            root.Children.Add(lowerGrid);

            var leftStack = new StackPanel();
            Grid.SetColumn(leftStack, 0);
            lowerGrid.Children.Add(leftStack);

            leftStack.Children.Add(BuildOverviewFlowCard());
            leftStack.Children.Add(BuildOverviewAppsCard());

            var rightStack = new StackPanel
            {
                Margin = new Thickness(18, 0, 0, 0)
            };
            Grid.SetColumn(rightStack, 1);
            lowerGrid.Children.Add(rightStack);

            rightStack.Children.Add(BuildOverviewSafetyCard());
            rightStack.Children.Add(BuildOverviewStorageCard());

            return scroll;
        }

        private UIElement BuildOverviewWelcomeCard()
        {
            var card = CreateCardShell();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.2, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(0.8, GridUnitType.Star)
            });
            card.Child = grid;

            var left = new StackPanel();
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            left.Children.Add(new TextBlock
            {
                Text = "Quick cleanup, without guesswork",
                FontFamily = HeadingFontFamily(),
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            });

            left.Children.Add(new TextBlock
            {
                Text = "The refreshed layout keeps the main actions visible at all times: scan, review large target cards, then clean only what you selected.",
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            var chipPanel = new WrapPanel
            {
                Margin = new Thickness(0, 18, 0, 0)
            };
            chipPanel.Children.Add(BuildSoftChip("Guided flow"));
            chipPanel.Children.Add(BuildSoftChip("Clear file preview"));
            chipPanel.Children.Add(BuildSoftChip("Action logs"));
            left.Children.Add(chipPanel);

            var right = new Border
            {
                Margin = new Thickness(22, 0, 0, 0),
                Padding = new Thickness(20),
                Background = SurfaceMutedBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22)
            };
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            var rightStack = new StackPanel();
            right.Child = rightStack;

            rightStack.Children.Add(new TextBlock
            {
                Text = hasScanRun ? "Latest scan" : "Start here",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            });

            rightStack.Children.Add(new TextBlock
            {
                Text = hasScanRun && currentSnapshot != null
                    ? string.Format("{0} cleanable item(s) / {1}", currentSnapshot.CleanableCount, SizeFormatter.Format(currentSnapshot.CleanableBytes))
                    : "Run Scan now to fill the cleanup workspace.",
                Margin = new Thickness(0, 8, 0, 0),
                FontFamily = HeadingFontFamily(),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = PrimaryTextBrush()
            });

            rightStack.Children.Add(new TextBlock
            {
                Text = hasScanRun
                    ? "Open Cleanup to review the target cards and start with the selected areas only."
                    : "The first scan inspects temporary files and browser cache only. Nothing is deleted during the scan.",
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            var actionRow = new Grid
            {
                Margin = new Thickness(0, 18, 0, 0)
            };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            rightStack.Children.Add(actionRow);

            var scanButton = BuildPrimaryButton(hasScanRun ? "Scan again" : "Scan now", false);
            scanButton.Click += delegate { RefreshScanOnly(); };
            Grid.SetColumn(scanButton, 0);
            actionRow.Children.Add(scanButton);

            var openCleanupButton = BuildGhostButton("Open cleanup", false);
            openCleanupButton.Margin = new Thickness(10, 0, 0, 0);
            openCleanupButton.IsEnabled = !operationInProgress;
            openCleanupButton.Click += delegate { SwitchSection(NavigationSection.Cleanup); };
            Grid.SetColumn(openCleanupButton, 1);
            actionRow.Children.Add(openCleanupButton);

            return card;
        }

        private UIElement BuildOverviewFlowCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(BuildCardHeader(
                "How cleanup works",
                "The flow is intentionally simple so the next action is always obvious."));

            stack.Children.Add(BuildStepRow("01", "Scan", "Inspect temp folders and supported browser cache locations."));
            stack.Children.Add(BuildStepRow("02", "Select", "Use the large target cards to decide which areas should be cleaned."));
            stack.Children.Add(BuildStepRow("03", "Clean", "Move selected files to the Recycle Bin and write an action log."));

            return card;
        }

        private UIElement BuildOverviewAppsCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(BuildCardHeader(
                "Installed app snapshot",
                "A quick glance at what was loaded from Windows. Open Installed apps for the full searchable list."));

            if (allInstalledApps.Count == 0)
            {
                stack.Children.Add(BuildEmptyState("No app entries loaded yet."));
            }
            else
            {
                int shown = 0;
                foreach (var app in allInstalledApps.Take(8))
                {
                    shown++;
                    stack.Children.Add(BuildInfoRow(
                        app.Name,
                        string.IsNullOrWhiteSpace(app.Version) ? app.Publisher : app.Version));
                }

                stack.Children.Add(new TextBlock
                {
                    Text = string.Format("Showing {0} of {1} apps here.", shown, allInstalledApps.Count),
                    Margin = new Thickness(0, 12, 0, 0),
                    FontSize = 12,
                    Foreground = SecondaryTextBrush()
                });
            }

            return card;
        }

        private UIElement BuildOverviewSafetyCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(BuildCardHeader(
                "What stays untouched",
                "The rules below are always in effect, including when browsers are part of the selection."));

            stack.Children.Add(BuildHintRow("Passwords and forms", "Browser cleanup is limited to cache folders only."));
            stack.Children.Add(BuildHintRow("Documents and media", "Common personal folders and sensitive extensions stay excluded."));
            stack.Children.Add(BuildHintRow("Permanent deletion", "Cleanup moves items to the Recycle Bin."));
            stack.Children.Add(BuildHintRow("Audit trail", "Each cleanup writes a timestamped action log."));

            return card;
        }

        private UIElement BuildOverviewStorageCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(BuildCardHeader(
                currentSnapshot == null ? "System drive" : currentSnapshot.SystemDriveLabel,
                currentSnapshot == null ? "Drive information is loading." : currentSnapshot.SystemDriveUsage));

            var progress = new ProgressBar
            {
                Height = 12,
                Minimum = 0,
                Maximum = 100,
                Value = currentSnapshot == null ? 0 : currentSnapshot.SystemDriveUsagePercent,
                Margin = new Thickness(0, 8, 0, 0)
            };
            stack.Children.Add(progress);

            stack.Children.Add(new TextBlock
            {
                Text = currentSnapshot == null
                    ? "Waiting for storage information."
                    : string.Format("{0:0}% used on the system drive.", currentSnapshot.SystemDriveUsagePercent),
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 12,
                Foreground = SecondaryTextBrush()
            });

            return card;
        }

        private UIElement BuildCleanupSection()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            scroll.Content = root;

            root.Children.Add(BuildCleanupIntroCard());

            if (!hasScanRun)
            {
                root.Children.Add(BuildCleanupStartCard());
                return scroll;
            }

            var layout = new Grid
            {
                Margin = new Thickness(0, 20, 0, 0)
            };
            layout.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.4, GridUnitType.Star)
            });
            layout.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(420)
            });
            root.Children.Add(layout);

            var targetsCard = BuildCleanupTargetsCard();
            Grid.SetColumn(targetsCard, 0);
            layout.Children.Add(targetsCard);

            var selectionCard = BuildCleanupSelectionCard();
            Grid.SetColumn(selectionCard, 1);
            var selectionElement = selectionCard as FrameworkElement;
            if (selectionElement != null)
            {
                selectionElement.Margin = new Thickness(18, 0, 0, 0);
            }
            layout.Children.Add(selectionCard);

            var previewCard = BuildCleanupPreviewCard();
            var previewElement = previewCard as FrameworkElement;
            if (previewElement != null)
            {
                previewElement.Margin = new Thickness(0, 18, 0, 0);
            }
            root.Children.Add(previewCard);

            return scroll;
        }

        private UIElement BuildCleanupIntroCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            cleanerSummaryText = new TextBlock
            {
                FontFamily = HeadingFontFamily(),
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            };
            stack.Children.Add(cleanerSummaryText);

            cleanupActionHintText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 16),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = AccentBrush()
            };
            stack.Children.Add(cleanupActionHintText);

            var steps = new UniformGrid
            {
                Columns = 2
            };
            stack.Children.Add(steps);

            steps.Children.Add(BuildStepCard("1", "Scan", !hasScanRun, hasScanRun));
            steps.Children.Add(BuildStepCard("2", "Select", hasScanRun && GetSelectedEntries().Count() == 0, GetSelectedEntries().Any()));

            return card;
        }

        private UIElement BuildCleanupTargetsCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(BuildCardHeader(
                "2. Choose areas",
                "Click one or more areas."));

            cleanupTargetPanel = new WrapPanel
            {
                Margin = new Thickness(-6, 2, -6, -6),
                ItemWidth = 290
            };
            stack.Children.Add(cleanupTargetPanel);

            return card;
        }

        private UIElement BuildCleanupPreviewCard()
        {
            var card = CreateCardShell();
            card.Padding = new Thickness(20, 20, 20, 18);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(340)
            });
            card.Child = grid;

            var header = new StackPanel();
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            header.Children.Add(BuildCardHeader(
                "Files (sample)",
                "For review before cleanup."));

            cleanupPreviewCaptionText = new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 12),
                FontSize = 12,
                Foreground = AccentBrush()
            };
            header.Children.Add(cleanupPreviewCaptionText);

            cleanupPreviewGrid = BuildPreviewGrid();
            Grid.SetRow(cleanupPreviewGrid, 1);
            grid.Children.Add(cleanupPreviewGrid);

            return card;
        }

        private UIElement BuildCleanupSelectionCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(BuildCardHeader(
                "3. Start cleanup",
                "This shows what is currently selected."));

            selectionModeText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            };
            stack.Children.Add(selectionModeText);

            selectionSummaryText = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                FontFamily = HeadingFontFamily(),
                FontSize = 34,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = PrimaryTextBrush()
            };
            stack.Children.Add(selectionSummaryText);

            cleanupSelectionDetailsText = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 16),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            };
            stack.Children.Add(cleanupSelectionDetailsText);

            var buttonGrid = new Grid();
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            stack.Children.Add(buttonGrid);

            var selectAllButton = BuildGhostButton("Select all", false);
            selectAllButton.IsEnabled = hasScanRun && !operationInProgress;
            selectAllButton.Click += delegate { SelectVisibleCleanableItems(); };
            Grid.SetColumn(selectAllButton, 0);
            buttonGrid.Children.Add(selectAllButton);

            var clearButton = BuildGhostButton("Clear selection", false);
            clearButton.Margin = new Thickness(10, 0, 0, 0);
            clearButton.IsEnabled = hasScanRun && !operationInProgress;
            clearButton.Click += delegate { ClearSelection(); };
            Grid.SetColumn(clearButton, 1);
            buttonGrid.Children.Add(clearButton);

            cleanupActionButton = BuildPrimaryButton("3. Start cleanup", true);
            cleanupActionButton.Margin = new Thickness(0, 12, 0, 0);
            cleanupActionButton.Click += delegate { MoveSelectedItemsToRecycleBin(); };
            stack.Children.Add(cleanupActionButton);

            return card;
        }

        private Border BuildStepCard(string stepNumber, string title, bool isActive, bool isDone)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 12, 0),
                Padding = new Thickness(16),
                Background = isActive ? SelectedTileBackgroundBrush() : SurfaceMutedBrush(),
                BorderBrush = isActive || isDone ? AccentBrush() : OutlineBrush(),
                BorderThickness = new Thickness(isActive ? 2 : 1),
                CornerRadius = new CornerRadius(18)
            };

            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(new TextBlock
            {
                Text = stepNumber,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = title,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = isDone ? "Done" : isActive ? "Now" : "Next",
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11,
                Foreground = SecondaryTextBrush()
            });

            return card;
        }

        private UIElement BuildCleanupStartCard()
        {
            var card = CreateCardShell();
            card.Padding = new Thickness(28);

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            card.Child = stack;

            stack.Children.Add(new TextBlock
            {
                Text = "Start with step 1",
                FontFamily = HeadingFontFamily(),
                FontSize = 32,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = PrimaryTextBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Click the scan button. After that you can choose the areas.",
                Margin = new Thickness(0, 10, 0, 22),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = SecondaryTextBrush()
            });

            var button = BuildPrimaryButton("1. Start scan", true);
            button.Width = 260;
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.Click += delegate { RefreshScanOnly(); };
            stack.Children.Add(button);

            return card;
        }

        private UIElement BuildCleanupBreakdownCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            cleanupBreakdownTitleText = new TextBlock
            {
                Text = "Breakdown",
                FontFamily = HeadingFontFamily(),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            };
            stack.Children.Add(cleanupBreakdownTitleText);

            stack.Children.Add(new TextBlock
            {
                Text = "Review where the current total comes from. Selected targets stay highlighted here too.",
                Margin = new Thickness(0, 6, 0, 16),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            cleanupBreakdownPanel = new StackPanel();
            stack.Children.Add(cleanupBreakdownPanel);

            return card;
        }

        private UIElement BuildCleanupSafetyCard()
        {
            var card = CreateCardShell();
            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(BuildCardHeader(
                "Before cleanup starts",
                "These notes are always visible so the user does not have to remember hidden rules."));

            stack.Children.Add(BuildHintRow("Recycle Bin", "Selected files are moved to the Recycle Bin, not permanently deleted."));
            stack.Children.Add(BuildHintRow("Browsers", "If browser cache is selected, supported browsers are closed before cleanup begins."));
            stack.Children.Add(BuildHintRow("Excluded data", "Passwords, saved forms, personal files, archives, and database files remain blocked."));
            stack.Children.Add(BuildHintRow("Last action", string.IsNullOrWhiteSpace(lastActionMessage) ? "No cleanup has been run yet." : lastActionMessage));

            return card;
        }

        private UIElement BuildAppsSection()
        {
            var card = CreateCardShell();
            card.Margin = new Thickness(0, 0, 0, 0);

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            layout.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            card.Child = layout;

            var toolbar = BuildAppsToolbar();
            Grid.SetRow(toolbar, 0);
            layout.Children.Add(toolbar);

            var scrollViewer = new ScrollViewer
            {
                Margin = new Thickness(0, 18, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scrollViewer, 1);
            layout.Children.Add(scrollViewer);

            appsTilePanel = new WrapPanel
            {
                Margin = new Thickness(0),
                ItemWidth = 228
            };
            scrollViewer.Content = appsTilePanel;

            return card;
        }

        private UIElement BuildAppsToolbar()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(144)
            });
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(320)
            });
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(210)
            });
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(210)
            });

            var left = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            appsSummaryText = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            };
            left.Children.Add(appsSummaryText);

            var refreshButton = BuildGhostButton("Refresh", false);
            refreshButton.Height = 38;
            refreshButton.Margin = new Thickness(18, 0, 0, 0);
            refreshButton.Click += delegate { RefreshInstalledAppsFromToolbar(); };
            Grid.SetColumn(refreshButton, 1);
            root.Children.Add(refreshButton);

            appSearchBox = new TextBox
            {
                Height = 38,
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Background = SurfaceMutedBrush(),
                BorderBrush = AccentOutlineBrush(),
                BorderThickness = new Thickness(1),
                Foreground = PrimaryTextBrush(),
                Text = appSearchQuery,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            appSearchBox.TextChanged += delegate
            {
                appSearchQuery = appSearchBox.Text ?? string.Empty;
                ApplyAppFilter();
            };
            Grid.SetColumn(appSearchBox, 2);
            root.Children.Add(appSearchBox);

            appsFilterComboBox = BuildAppsOptionComboBox(new[]
            {
                CreateOption("all", "Filter: All"),
                CreateOption("uninstallable", "Uninstallable only"),
                CreateOption("size_large", "Size > 1 GB"),
                CreateOption("size_known", "Size known"),
                CreateOption("date_recent", "Last 90 days"),
                CreateOption("date_old", "Older than 1 year"),
                CreateOption("machine", "Machine only"),
                CreateOption("user", "User only")
            }, selectedAppsFilterKey);
            appsFilterComboBox.SelectionChanged += delegate
            {
                var selected = appsFilterComboBox.SelectedItem as SelectionOption;
                selectedAppsFilterKey = selected == null ? "all" : selected.Key;
                ApplyAppFilter();
            };
            Grid.SetColumn(appsFilterComboBox, 3);
            root.Children.Add(appsFilterComboBox);

            appsSortComboBox = BuildAppsOptionComboBox(new[]
            {
                CreateOption("name", "Sort: Name"),
                CreateOption("size_desc", "Sort: Largest"),
                CreateOption("size_asc", "Sort: Smallest"),
                CreateOption("date_desc", "Sort: Newest"),
                CreateOption("date_asc", "Sort: Oldest")
            }, selectedAppsSortKey);
            appsSortComboBox.Margin = new Thickness(12, 0, 0, 0);
            appsSortComboBox.SelectionChanged += delegate
            {
                var selected = appsSortComboBox.SelectedItem as SelectionOption;
                selectedAppsSortKey = selected == null ? "name" : selected.Key;
                ApplyAppFilter();
            };
            Grid.SetColumn(appsSortComboBox, 4);
            root.Children.Add(appsSortComboBox);

            return root;
        }

        private UIElement BuildStartupSection()
        {
            var card = CreateCardShell();
            card.Margin = new Thickness(0);

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            layout.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            card.Child = layout;

            var toolbar = BuildStartupToolbar();
            Grid.SetRow(toolbar, 0);
            layout.Children.Add(toolbar);

            var scrollViewer = new ScrollViewer
            {
                Margin = new Thickness(0, 18, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scrollViewer, 1);
            layout.Children.Add(scrollViewer);

            startupItemPanel = new StackPanel();
            scrollViewer.Content = startupItemPanel;

            return card;
        }

        private UIElement BuildStartupToolbar()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(144)
            });
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(320)
            });
            root.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(210)
            });

            var left = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            startupSummaryText = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            };
            left.Children.Add(startupSummaryText);

            var refreshButton = BuildGhostButton("Refresh", false);
            refreshButton.Height = 38;
            refreshButton.Margin = new Thickness(18, 0, 0, 0);
            refreshButton.Click += delegate { RefreshStartupItemsFromToolbar(); };
            Grid.SetColumn(refreshButton, 1);
            root.Children.Add(refreshButton);

            startupSearchBox = new TextBox
            {
                Height = 38,
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Background = SurfaceMutedBrush(),
                BorderBrush = AccentOutlineBrush(),
                BorderThickness = new Thickness(1),
                Foreground = PrimaryTextBrush(),
                Text = startupSearchQuery,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            startupSearchBox.TextChanged += delegate
            {
                startupSearchQuery = startupSearchBox.Text ?? string.Empty;
                ApplyStartupFilter();
            };
            Grid.SetColumn(startupSearchBox, 2);
            root.Children.Add(startupSearchBox);

            startupFilterComboBox = BuildAppsOptionComboBox(new[]
            {
                CreateOption("all", "Filter: All"),
                CreateOption("enabled", "Enabled only"),
                CreateOption("disabled", "Disabled only"),
                CreateOption("machine", "Machine only"),
                CreateOption("user", "User only")
            }, selectedStartupFilterKey);
            startupFilterComboBox.SelectionChanged += delegate
            {
                var selected = startupFilterComboBox.SelectedItem as SelectionOption;
                selectedStartupFilterKey = selected == null ? "all" : selected.Key;
                ApplyStartupFilter();
            };
            Grid.SetColumn(startupFilterComboBox, 3);
            root.Children.Add(startupFilterComboBox);

            return root;
        }

        private UIElement BuildStartupItemRow(StartupItemInfo item)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16),
                Background = item != null && item.IsEnabled ? SelectedTileBackgroundBrush() : SurfaceMutedBrush(),
                BorderBrush = item != null && item.IsEnabled ? AccentOutlineBrush() : OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20)
            };

            if (item == null)
            {
                return card;
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            card.Child = grid;

            var checkBox = new CheckBox
            {
                Margin = new Thickness(0, 4, 16, 0),
                VerticalAlignment = VerticalAlignment.Top,
                IsChecked = item.IsEnabled,
                IsEnabled = !startupReloadInProgress && !startupToggleInProgress
            };
            checkBox.Checked += delegate { ToggleStartupItem(item, true); };
            checkBox.Unchecked += delegate { ToggleStartupItem(item, false); };
            Grid.SetColumn(checkBox, 0);
            grid.Children.Add(checkBox);

            var content = new StackPanel();
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);

            content.Children.Add(new TextBlock
            {
                Text = item.Name,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = PrimaryTextBrush()
            });

            content.Children.Add(new TextBlock
            {
                Text = string.Format("{0}  |  {1}", item.Scope, item.SourceKind),
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            });

            content.Children.Add(new TextBlock
            {
                Text = item.Command,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            if (!string.IsNullOrWhiteSpace(item.Location))
            {
                content.Children.Add(new TextBlock
                {
                    Text = item.Location,
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = SecondaryTextBrush()
                });
            }

            var badgeHost = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(badgeHost, 2);
            grid.Children.Add(badgeHost);

            badgeHost.Children.Add(BuildStartupStateBadge(item.IsEnabled));

            return card;
        }

        private Border BuildStartupStateBadge(bool isEnabled)
        {
            var badge = new Border
            {
                Padding = new Thickness(12, 6, 12, 6),
                Background = isEnabled ? AccentMutedBrush() : OutlineBrush(),
                BorderBrush = isEnabled ? AccentOutlineBrush() : OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999)
            };
            badge.Child = new TextBlock
            {
                Text = isEnabled ? "Enabled" : "Disabled",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = isEnabled ? AccentBrush() : SecondaryTextBrush()
            };
            return badge;
        }

        private Border CreateCardShell()
        {
            return new Border
            {
                Background = SurfaceBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 18)
            };
        }

        private ComboBox BuildAppsOptionComboBox(IEnumerable<SelectionOption> options, string selectedKey)
        {
            var comboBox = new ComboBox
            {
                Height = 38,
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Background = SurfaceMutedBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                Foreground = PrimaryTextBrush(),
                Style = BuildComboBoxStyle(),
                ItemsSource = options == null ? new List<SelectionOption>() : options.ToList(),
                SelectedValuePath = "Key",
                DisplayMemberPath = "Label",
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemContainerStyle = BuildComboBoxItemStyle()
            };
            comboBox.Resources[SystemColors.WindowBrushKey] = SurfaceMutedBrush();
            comboBox.Resources[SystemColors.ControlBrushKey] = SurfaceMutedBrush();
            comboBox.Resources[SystemColors.WindowTextBrushKey] = PrimaryTextBrush();
            comboBox.Resources[SystemColors.HighlightBrushKey] = SelectedTileBackgroundBrush();
            comboBox.Resources[SystemColors.HighlightTextBrushKey] = PrimaryTextBrush();

            comboBox.SelectedValue = selectedKey;
            if (comboBox.SelectedItem == null && comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }

            return comboBox;
        }

        private Style BuildComboBoxStyle()
        {
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceMutedBrush()));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, AccentOutlineBrush()));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush()));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
            style.Setters.Add(new Setter(Control.TemplateProperty, BuildComboBoxTemplate()));

            var openTrigger = new Trigger
            {
                Property = ComboBox.IsDropDownOpenProperty,
                Value = true
            };
            openTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush()));
            style.Triggers.Add(openTrigger);

            return style;
        }

        private ControlTemplate BuildComboBoxTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));

            var root = new FrameworkElementFactory(typeof(Grid));

            var outerBorder = new FrameworkElementFactory(typeof(Border));
            outerBorder.Name = "OuterBorder";
            outerBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            outerBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            outerBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            outerBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));
            root.AppendChild(outerBorder);

            var layout = new FrameworkElementFactory(typeof(DockPanel));
            outerBorder.AppendChild(layout);

            var arrowHost = new FrameworkElementFactory(typeof(Border));
            arrowHost.SetValue(DockPanel.DockProperty, Dock.Right);
            arrowHost.SetValue(FrameworkElement.WidthProperty, 34.0);
            arrowHost.SetValue(Border.BorderBrushProperty, OutlineBrush());
            arrowHost.SetValue(Border.BorderThicknessProperty, new Thickness(1, 0, 0, 0));
            arrowHost.SetValue(Border.BackgroundProperty, SurfaceBrush());
            layout.AppendChild(arrowHost);

            var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            arrow.SetValue(System.Windows.Shapes.Shape.FillProperty, SecondaryTextBrush());
            arrow.SetValue(FrameworkElement.WidthProperty, 10.0);
            arrow.SetValue(FrameworkElement.HeightProperty, 6.0);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 0 0 L 5 5 L 10 0 Z"));
            arrowHost.AppendChild(arrow);

            var selectionPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            selectionPresenter.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 12, 0));
            selectionPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            selectionPresenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            selectionPresenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            selectionPresenter.SetValue(ContentPresenter.ContentStringFormatProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemStringFormatProperty));
            selectionPresenter.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            layout.AppendChild(selectionPresenter);

            var hitTarget = new FrameworkElementFactory(typeof(ToggleButton));
            hitTarget.SetValue(UIElement.OpacityProperty, 0.0);
            hitTarget.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            hitTarget.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            hitTarget.SetValue(Control.FocusVisualStyleProperty, null);
            hitTarget.SetValue(ButtonBase.ClickModeProperty, ClickMode.Press);
            hitTarget.SetValue(Control.IsTabStopProperty, false);
            hitTarget.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = BindingMode.TwoWay
            });
            root.AppendChild(hitTarget);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            root.AppendChild(popup);

            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
            popupBorder.SetValue(Border.BackgroundProperty, SurfaceBrush());
            popupBorder.SetValue(Border.BorderBrushProperty, AccentOutlineBrush());
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            popupBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            popup.AppendChild(popupBorder);

            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scrollViewer.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scrollViewer.SetValue(FrameworkElement.MaxHeightProperty, 320.0);
            popupBorder.AppendChild(scrollViewer);

            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            itemsPresenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            scrollViewer.AppendChild(itemsPresenter);

            template.VisualTree = root;
            return template;
        }

        private Style BuildComboBoxItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceMutedBrush()));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush()));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            var hoverTrigger = new Trigger
            {
                Property = ComboBoxItem.IsHighlightedProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, AccentMutedBrush()));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush()));
            style.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger
            {
                Property = ComboBoxItem.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, SelectedTileBackgroundBrush()));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush()));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        private static SelectionOption CreateOption(string key, string label)
        {
            return new SelectionOption
            {
                Key = key,
                Label = label
            };
        }

        private UIElement BuildCardHeader(string title, string subtitle)
        {
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = HeadingFontFamily(),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            });

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = SecondaryTextBrush()
                });
            }

            return stack;
        }

        private Border BuildMetricCard(string value, string label, string detail)
        {
            var card = CreateCardShell();
            card.Margin = new Thickness(0, 0, 18, 0);
            card.Padding = new Thickness(18);

            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = SecondaryTextBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = value,
                Margin = new Thickness(0, 10, 0, 0),
                FontFamily = HeadingFontFamily(),
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = detail,
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            return card;
        }

        private Border BuildSoftChip(string label)
        {
            var chip = new Border
            {
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(12, 7, 12, 7),
                Background = AccentMutedBrush(),
                BorderBrush = AccentOutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999)
            };
            chip.Child = new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            };
            return chip;
        }

        private UIElement BuildStepRow(string step, string title, string detail)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 16, 0, 0),
                Padding = new Thickness(14),
                Background = SurfaceMutedBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(54)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.Child = grid;

            var badge = new Border
            {
                Width = 40,
                Height = 40,
                Background = AccentBrush(),
                CornerRadius = new CornerRadius(20),
                VerticalAlignment = VerticalAlignment.Top
            };
            badge.Child = new TextBlock
            {
                Text = step,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = OnAccentTextBrush()
            };
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            var text = new StackPanel
            {
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            text.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            });

            text.Children.Add(new TextBlock
            {
                Text = detail,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            return row;
        }

        private UIElement BuildHintRow(string title, string detail)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(120)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            var label = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var value = new TextBlock
            {
                Text = detail,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            return row;
        }

        private UIElement BuildInfoRow(string title, string detail)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(14),
                Background = SurfaceMutedBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16)
            };

            var stack = new StackPanel();
            border.Child = stack;

            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = PrimaryTextBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = detail,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            return border;
        }

        private UIElement BuildEmptyState(string message)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(16),
                Background = SurfaceMutedBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18)
            };
            border.Child = new TextBlock
            {
                Text = message,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            };
            return border;
        }

        private UIElement BuildInstalledAppTile(InstalledAppInfo app)
        {
            var card = new Border
            {
                Width = 212,
                Margin = new Thickness(0, 0, 16, 16),
                Padding = new Thickness(16),
                Background = SurfaceMutedBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                VerticalAlignment = VerticalAlignment.Top
            };
            card.ToolTip = BuildInstalledAppTooltip(app);

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            card.Child = stack;

            stack.Children.Add(BuildInstalledAppIcon(app));

            stack.Children.Add(new TextBlock
            {
                Text = app.Name,
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = PrimaryTextBrush()
            });

            var button = BuildGhostButton(app.CanUninstall ? "Uninstall" : "Unavailable", false);
            button.Margin = new Thickness(0, 14, 0, 0);
            button.IsEnabled = app.CanUninstall;
            button.Click += delegate { OpenInstalledAppUninstaller(app); };
            stack.Children.Add(button);

            return card;
        }

        private string BuildInstalledAppTooltip(InstalledAppInfo app)
        {
            if (app == null)
            {
                return string.Empty;
            }

            string sizeText = app.EstimatedSizeBytes > 0 ? SizeFormatter.Format(app.EstimatedSizeBytes) : "unknown";
            string dateText = app.InstallDate.HasValue ? app.InstallDate.Value.ToString("yyyy-MM-dd") : "unknown";

            return string.Format(
                "Name: {0}\nSize: {1}\nInstalled: {2}\nScope: {3}",
                app.Name,
                sizeText,
                dateText,
                app.Scope);
        }

        private UIElement BuildInstalledAppIcon(InstalledAppInfo app)
        {
            var source = LoadInstalledAppIconSource(app == null ? null : app.DisplayIconPath, 56);
            if (source != null)
            {
                return new Image
                {
                    Width = 56,
                    Height = 56,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Source = source
                };
            }

            var fallback = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(28),
                Background = AccentMutedBrush(),
                BorderBrush = AccentOutlineBrush(),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            string label = "APP";
            if (app != null && !string.IsNullOrWhiteSpace(app.Name))
            {
                var parts = app.Name
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Take(2)
                    .Select(item => item.Substring(0, 1).ToUpperInvariant())
                    .ToList();

                if (parts.Count > 0)
                {
                    label = string.Join(string.Empty, parts);
                }
            }

            fallback.Child = new TextBlock
            {
                Text = label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            };

            return fallback;
        }

        private ImageSource LoadInstalledAppIconSource(string rawDisplayIconPath, int size)
        {
            string iconPath = NormalizeInstalledAppIconPath(rawDisplayIconPath);
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                return null;
            }

            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath))
                {
                    if (icon == null)
                    {
                        return null;
                    }

                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(size, size));
                    source.Freeze();
                    return source;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeInstalledAppIconPath(string rawDisplayIconPath)
        {
            if (string.IsNullOrWhiteSpace(rawDisplayIconPath))
            {
                return string.Empty;
            }

            string value = rawDisplayIconPath.Trim();
            if (value.StartsWith("\"", StringComparison.OrdinalIgnoreCase))
            {
                int closingQuote = value.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    return value.Substring(1, closingQuote - 1);
                }
            }

            int commaIndex = value.LastIndexOf(',');
            if (commaIndex > 2)
            {
                string withoutIndex = value.Substring(0, commaIndex).Trim().Trim('"');
                if (File.Exists(withoutIndex))
                {
                    return withoutIndex;
                }
            }

            return value.Trim('"');
        }

        private void OpenInstalledAppUninstaller(InstalledAppInfo app)
        {
            if (app == null || !app.CanUninstall)
            {
                currentStatusMessage = "No uninstaller is available for this app.";
                UpdateStatusBarState();
                return;
            }

            var confirm = MessageBox.Show(
                "Open the uninstaller for \"" + app.Name + "\" now?",
                "Start uninstaller",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    Arguments = "/c start \"\" " + app.UninstallCommand,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Environment.SystemDirectory
                };

                Process.Start(startInfo);
                currentStatusMessage = "Uninstaller for " + app.Name + " started. The list will refresh automatically.";
                lastActionMessage = "Uninstaller for " + app.Name + " started.";
                UpdateStatusBarState();
                MonitorInstalledAppRemovalAsync(app);
            }
            catch (Exception ex)
            {
                currentStatusMessage = "Could not start the uninstaller: " + ex.Message;
                UpdateStatusBarState();
            }
        }

        private async void RefreshInstalledAppsFromToolbar()
        {
            if (appsReloadInProgress)
            {
                currentStatusMessage = "The app list is already refreshing.";
                UpdateStatusBarState();
                return;
            }

            await RefreshInstalledAppsAsync(
                "Refreshing app list...",
                "App list refreshed.");
        }

        private async Task<bool> RefreshInstalledAppsAsync(string startMessage, string completionMessage)
        {
            try
            {
                appsReloadInProgress = true;

                if (!string.IsNullOrWhiteSpace(startMessage))
                {
                    currentStatusMessage = startMessage;
                    UpdateStatusBarState();
                }

                allInstalledApps = await Task.Run(delegate
                {
                    return installedAppsService.Load();
                });

                currentSnapshot = systemInfoService.Build(allInstalledApps, allEntries);
                ApplyAppFilter();

                if (!string.IsNullOrWhiteSpace(completionMessage))
                {
                    currentStatusMessage = completionMessage;
                    UpdateStatusBarState();
                }

                return true;
            }
            catch (Exception ex)
            {
                currentStatusMessage = "Refresh failed: " + ex.Message;
                UpdateStatusBarState();
                return false;
            }
            finally
            {
                appsReloadInProgress = false;
            }
        }

        private async void MonitorInstalledAppRemovalAsync(InstalledAppInfo app)
        {
            if (app == null)
            {
                return;
            }

            int monitorToken = ++uninstallMonitorToken;

            for (int attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));

                if (monitorToken != uninstallMonitorToken)
                {
                    return;
                }

                List<InstalledAppInfo> latestApps;
                try
                {
                    latestApps = await Task.Run(delegate
                    {
                        return installedAppsService.Load();
                    });
                }
                catch
                {
                    return;
                }

                bool stillPresent = latestApps.Any(item => IsSameInstalledApp(item, app));
                if (stillPresent)
                {
                    continue;
                }

                allInstalledApps = latestApps;
                currentSnapshot = systemInfoService.Build(allInstalledApps, allEntries);
                ApplyAppFilter();
                    currentStatusMessage = app.Name + " was removed. App list refreshed.";
                lastActionMessage = currentStatusMessage;
                UpdateStatusBarState();
                return;
            }

            if (monitorToken == uninstallMonitorToken)
            {
                currentStatusMessage = app.Name + " is still visible. Click \"Refresh\" once the uninstall has finished.";
                UpdateStatusBarState();
            }
        }

        private static bool IsSameInstalledApp(InstalledAppInfo left, InstalledAppInfo right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.Name ?? string.Empty, right.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Version ?? string.Empty, right.Version ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Publisher ?? string.Empty, right.Publisher ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Scope ?? string.Empty, right.Scope ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private DataGrid BuildPreviewGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                IsReadOnly = true,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                RowBackground = SurfaceBrush(),
                AlternatingRowBackground = SurfaceMutedBrush(),
                Foreground = PrimaryTextBrush(),
                ColumnHeaderStyle = BuildDataGridHeaderStyle()
            };

            grid.Columns.Add(CreateTextColumn("Category", "Category", new DataGridLength(180), null));
            grid.Columns.Add(CreateTextColumn("Size", "SizeDisplay", new DataGridLength(100), null));
            grid.Columns.Add(CreateTextColumn("Modified", "LastWriteTime", new DataGridLength(145), "{0:yyyy-MM-dd HH:mm}"));
            grid.Columns.Add(CreateTextColumn("Path", "Path", new DataGridLength(1, DataGridLengthUnitType.Star), null));

            return grid;
        }

        private Style BuildDataGridHeaderStyle()
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceMutedBrush()));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, OutlineBrush()));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush()));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 10, 12, 10)));
            return style;
        }

        private static DataGridTextColumn CreateTextColumn(string header, string bindingPath, DataGridLength width, string format)
        {
            var binding = new Binding(bindingPath);
            if (!string.IsNullOrWhiteSpace(format))
            {
                binding.StringFormat = format;
            }

            return new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = width
            };
        }

        private Button BuildPrimaryButton(string label, bool large)
        {
            return new Button
            {
                Content = label,
                Height = large ? 46 : 40,
                Padding = large ? new Thickness(18, 12, 18, 12) : new Thickness(16, 10, 16, 10),
                FontSize = large ? 14 : 13,
                FontWeight = FontWeights.SemiBold,
                Background = AccentBrush(),
                Foreground = OnAccentTextBrush(),
                BorderBrush = AccentBorderBrush(),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private Button BuildSecondaryButton(string label, bool large)
        {
            return new Button
            {
                Content = label,
                Height = large ? 46 : 40,
                Padding = large ? new Thickness(18, 12, 18, 12) : new Thickness(16, 10, 16, 10),
                FontSize = large ? 14 : 13,
                FontWeight = FontWeights.SemiBold,
                Background = HeroButtonBrush(),
                Foreground = HeroPrimaryTextBrush(),
                BorderBrush = HeroOutlineBrush(),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private Button BuildGhostButton(string label, bool large)
        {
            return new Button
            {
                Content = label,
                Height = large ? 46 : 40,
                Padding = large ? new Thickness(18, 12, 18, 12) : new Thickness(16, 10, 16, 10),
                FontSize = large ? 14 : 13,
                FontWeight = FontWeights.SemiBold,
                Background = SurfaceMutedBrush(),
                Foreground = PrimaryTextBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private void RefreshDashboard()
        {
            if (operationInProgress)
            {
                return;
            }

            RefreshDashboardAsync();
        }

        private async void RefreshDashboardAsync()
        {
            try
            {
                BeginOperation("Loading interface...", "Loading installed apps...", true, 0, 0);

                var installedAppsTask = Task.Run(delegate
                {
                    return installedAppsService.Load();
                });

                var startupItemsTask = Task.Run(delegate
                {
                    return startupProgramsService.Load();
                });

                await Task.WhenAll(installedAppsTask, startupItemsTask);

                allInstalledApps = installedAppsTask.Result;
                allStartupItems = startupItemsTask.Result;
                visibleStartupItems = new List<StartupItemInfo>(allStartupItems);

                allEntries = new List<ScanEntry>();
                currentEntries = new List<ScanEntry>();
                currentSnapshot = systemInfoService.Build(allInstalledApps, allEntries);
                lastScanResult = null;
                hasScanRun = false;
                selectedCleanupTargets.Clear();
                lastActionMessage = "Installed apps loaded. Scan not started yet.";
                currentStatusMessage = "Ready. Start the first scan now.";

                ApplyAppFilter();
                EndOperation(currentStatusMessage);
                RenderCurrentSection();
            }
            catch (Exception ex)
            {
                EndOperation("Loading failed: " + ex.Message);
            }
        }

        private async void RefreshScanOnly()
        {
            if (operationInProgress)
            {
                currentStatusMessage = "Please wait. Another action is already running.";
                UpdateStatusBarState();
                return;
            }

            await RefreshScanOnlyInternalAsync();
        }

        private async Task RefreshScanOnlyInternalAsync()
        {
            try
            {
                BeginOperation("Scan is running...", "Preparing scan...", true, 0, 0);

                var progress = new Progress<OperationProgressInfo>(UpdateOperationProgress);
                var scanResult = await Task.Run(delegate
                {
                    return scanService.Run(progress);
                });

                allEntries = scanResult.Entries;
                currentEntries = new List<ScanEntry>(scanResult.Entries);
                currentSnapshot = systemInfoService.Build(allInstalledApps, allEntries);
                lastScanResult = scanResult;
                hasScanRun = true;
                selectedCleanupTargets.Clear();
                activeSection = NavigationSection.Cleanup;
                lastActionMessage = scanResult.Summary.TotalCount == 0
                    ? "Scan finished. No cleanable files were found."
                    : string.Format(
                        "Scan finished. Found {0} items / {1}.",
                        scanResult.Summary.SafeCount,
                        SizeFormatter.Format(currentSnapshot.CleanableBytes));
                currentStatusMessage = scanResult.Summary.TotalCount == 0
                    ? "No results were found in the supported areas."
                    : "Scan finished. Choose the areas for cleanup now.";

                EndOperation(currentStatusMessage);
                RenderCurrentSection();
            }
            catch (Exception ex)
            {
                EndOperation("Scan failed: " + ex.Message);
            }
        }

        private async void RefreshStartupItemsFromToolbar()
        {
            if (startupReloadInProgress || startupToggleInProgress)
            {
                currentStatusMessage = "The startup list is already refreshing.";
                UpdateStatusBarState();
                return;
            }

            await RefreshStartupItemsAsync(
                "Refreshing startup list...",
                "Startup list refreshed.");
        }

        private async Task<bool> RefreshStartupItemsAsync(string startMessage, string completionMessage)
        {
            bool loaded = false;
            bool succeeded = false;

            try
            {
                startupReloadInProgress = true;
                UpdateNavigationState();

                if (!string.IsNullOrWhiteSpace(startMessage))
                {
                    currentStatusMessage = startMessage;
                    UpdateStatusBarState();
                }

                allStartupItems = await Task.Run(delegate
                {
                    return startupProgramsService.Load();
                });
                loaded = true;

                if (!string.IsNullOrWhiteSpace(completionMessage))
                {
                    currentStatusMessage = completionMessage;
                }
                succeeded = true;
            }
            catch (Exception ex)
            {
                currentStatusMessage = "Could not refresh startup list: " + ex.Message;
            }
            finally
            {
                startupReloadInProgress = false;
                UpdateNavigationState();
            }

            if (loaded)
            {
                ApplyStartupFilter();
            }

            UpdateStatusBarState();
            return succeeded && loaded;
        }

        private void ApplyAppFilter()
        {
            string query = appSearchQuery == null ? string.Empty : appSearchQuery.Trim();

            IEnumerable<InstalledAppInfo> filtered = string.IsNullOrWhiteSpace(query)
                ? allInstalledApps
                : allInstalledApps.Where(item =>
                    ContainsIgnoreCase(item.Name, query) ||
                    ContainsIgnoreCase(item.Version, query) ||
                    ContainsIgnoreCase(item.Publisher, query) ||
                    ContainsIgnoreCase(item.Scope, query));

            if (string.Equals(selectedAppsFilterKey, "uninstallable", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => item.CanUninstall);
            }
            else if (string.Equals(selectedAppsFilterKey, "size_large", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => item.EstimatedSizeBytes >= 1024L * 1024L * 1024L);
            }
            else if (string.Equals(selectedAppsFilterKey, "size_known", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => item.EstimatedSizeBytes > 0);
            }
            else if (string.Equals(selectedAppsFilterKey, "date_recent", StringComparison.OrdinalIgnoreCase))
            {
                DateTime cutoff = DateTime.Today.AddDays(-90);
                filtered = filtered.Where(item => item.InstallDate.HasValue && item.InstallDate.Value.Date >= cutoff);
            }
            else if (string.Equals(selectedAppsFilterKey, "date_old", StringComparison.OrdinalIgnoreCase))
            {
                DateTime cutoff = DateTime.Today.AddYears(-1);
                filtered = filtered.Where(item => item.InstallDate.HasValue && item.InstallDate.Value.Date <= cutoff);
            }
            else if (string.Equals(selectedAppsFilterKey, "machine", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => string.Equals(item.Scope, "Machine", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(selectedAppsFilterKey, "user", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => string.Equals(item.Scope, "User", StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(selectedAppsSortKey, "size_desc", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered
                    .OrderByDescending(item => item.EstimatedSizeBytes)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
            }
            else if (string.Equals(selectedAppsSortKey, "size_asc", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered
                    .OrderBy(item => item.EstimatedSizeBytes <= 0 ? long.MaxValue : item.EstimatedSizeBytes)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
            }
            else if (string.Equals(selectedAppsSortKey, "date_desc", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered
                    .OrderByDescending(item => item.InstallDate.HasValue)
                    .ThenByDescending(item => item.InstallDate)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
            }
            else if (string.Equals(selectedAppsSortKey, "date_asc", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered
                    .OrderByDescending(item => item.InstallDate.HasValue)
                    .ThenBy(item => item.InstallDate)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                filtered = filtered
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Version, StringComparer.OrdinalIgnoreCase);
            }

            var filteredList = filtered.ToList();

            visibleApps.Clear();
            foreach (var app in filteredList)
            {
                visibleApps.Add(app);
            }

            if (appsSummaryText != null)
            {
                appsSummaryText.Text = string.Format(
                    "{0} of {1} apps visible",
                    filteredList.Count,
                    allInstalledApps.Count);
            }

            if (appsTilePanel != null)
            {
                appsTilePanel.Children.Clear();

                if (filteredList.Count == 0)
                {
                    appsTilePanel.Children.Add(BuildEmptyState("No apps match the current search or filter."));
                }

                foreach (var app in filteredList)
                {
                    appsTilePanel.Children.Add(BuildInstalledAppTile(app));
                }
            }

            if (activeSection == NavigationSection.Apps)
            {
                UpdateHeaderState();
                UpdateStatusBarState();
            }
        }

        private void ApplyStartupFilter()
        {
            string query = startupSearchQuery == null ? string.Empty : startupSearchQuery.Trim();

            IEnumerable<StartupItemInfo> filtered = string.IsNullOrWhiteSpace(query)
                ? allStartupItems
                : allStartupItems.Where(item =>
                    ContainsIgnoreCase(item.Name, query) ||
                    ContainsIgnoreCase(item.Command, query) ||
                    ContainsIgnoreCase(item.Scope, query) ||
                    ContainsIgnoreCase(item.SourceKind, query) ||
                    ContainsIgnoreCase(item.Location, query));

            if (string.Equals(selectedStartupFilterKey, "enabled", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => item.IsEnabled);
            }
            else if (string.Equals(selectedStartupFilterKey, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => !item.IsEnabled);
            }
            else if (string.Equals(selectedStartupFilterKey, "machine", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => string.Equals(item.Scope, "Machine", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(selectedStartupFilterKey, "user", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => string.Equals(item.Scope, "User", StringComparison.OrdinalIgnoreCase));
            }

            visibleStartupItems = filtered
                .OrderByDescending(item => item.IsEnabled)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (startupSummaryText != null)
            {
                startupSummaryText.Text = string.Format(
                    "{0} of {1} startup entries visible",
                    visibleStartupItems.Count,
                    allStartupItems.Count);
            }

            if (startupItemPanel != null)
            {
                startupItemPanel.Children.Clear();

                if (visibleStartupItems.Count == 0)
                {
                    startupItemPanel.Children.Add(BuildEmptyState("No startup entries match the current search or filter."));
                }

                foreach (var item in visibleStartupItems)
                {
                    startupItemPanel.Children.Add(BuildStartupItemRow(item));
                }
            }

            if (activeSection == NavigationSection.Startup)
            {
                UpdateHeaderState();
                UpdateStatusBarState();
            }
        }

        private async void ToggleStartupItem(StartupItemInfo item, bool isEnabled)
        {
            if (item == null)
            {
                return;
            }

            if (startupToggleInProgress || startupReloadInProgress)
            {
                currentStatusMessage = "Please wait. The previous startup change is still running.";
                UpdateStatusBarState();
                ApplyStartupFilter();
                return;
            }

            startupToggleInProgress = true;
            UpdateNavigationState();

            string completionMessage = string.Empty;
            string errorMessage = string.Empty;
            bool showAdminWarning = false;

            try
            {
                currentStatusMessage = string.Format(
                    "Startup for {0} is being {1}...",
                    item.Name,
                    isEnabled ? "enabled" : "disabled");
                UpdateStatusBarState();

                await Task.Run(delegate
                {
                    startupProgramsService.SetEnabled(item, isEnabled);
                });

                allStartupItems = await Task.Run(delegate
                {
                    return startupProgramsService.Load();
                });

                ApplyStartupFilter();

                completionMessage = string.Format(
                    "{0} was {1}.",
                    item.Name,
                    isEnabled ? "enabled" : "disabled");
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = "Administrator rights are required for this startup entry.";
                showAdminWarning = true;
            }
            catch (Exception ex)
            {
                errorMessage = "Could not change the startup entry: " + ex.Message;
            }

            allStartupItems = await Task.Run(delegate
            {
                return startupProgramsService.Load();
            });

            startupToggleInProgress = false;
            UpdateNavigationState();

            ApplyStartupFilter();

            currentStatusMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? completionMessage
                : errorMessage;

            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                lastActionMessage = currentStatusMessage;
            }

            UpdateStatusBarState();

            if (showAdminWarning)
            {
                MessageBox.Show(
                    "This startup entry could not be changed.\n\nSystem entries often require administrator rights.",
                    "Could not change startup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void UpdateCleanupView()
        {
            var selectedKeys = GetSelectedTargetKeys();
            var selectedEntries = GetSelectedEntries().ToList();
            long totalCleanableBytes = allEntries.Where(item => item.IsCleanable).Sum(item => item.SizeBytes);
            int totalCleanableCount = allEntries.Count(item => item.IsCleanable);
            long selectedBytes = selectedEntries.Sum(item => item.SizeBytes);
            int blockedSelectedCount = selectedKeys.Sum(delegate(string key)
            {
                return GetTargetEntries(key).Count(item => !item.IsCleanable);
            });

            currentEntries = selectedEntries.Count > 0
                ? selectedEntries
                : new List<ScanEntry>(allEntries);

            if (cleanerSummaryText != null)
            {
                if (!hasScanRun)
                {
                    cleanerSummaryText.Text = "Step 1 of 3";
                }
                else if (totalCleanableCount == 0)
                {
                    cleanerSummaryText.Text = "Scan complete";
                }
                else
                {
                    cleanerSummaryText.Text = string.Format(
                        "Scan complete: {0} items / {1}",
                        totalCleanableCount,
                        SizeFormatter.Format(totalCleanableBytes));
                }
            }

            if (cleanupActionHintText != null)
            {
                cleanupActionHintText.Text = !hasScanRun
                    ? "Click \"1. Start scan\"."
                    : selectedEntries.Count == 0
                        ? "Click an area or \"Select all\"."
                        : "Click \"3. Start cleanup\".";
            }

            if (selectionModeText != null)
            {
                selectionModeText.Text = !hasScanRun
                    ? "Status"
                    : selectedEntries.Count == 0
                        ? "Nothing selected yet"
                        : "Selected";
            }

            if (selectionSummaryText != null)
            {
                selectionSummaryText.Text = !hasScanRun
                    ? "No scan yet"
                    : selectedEntries.Count == 0
                        ? "Nothing selected"
                        : SizeFormatter.Format(selectedBytes);
            }

            if (cleanupSelectionDetailsText != null)
            {
                if (!hasScanRun)
                {
                    cleanupSelectionDetailsText.Text = "Cleanup is not available until after a scan.";
                }
                else if (selectedEntries.Count == 0)
                {
                    cleanupSelectionDetailsText.Text = string.Format(
                        "{0} items are available.",
                        totalCleanableCount);
                }
                else
                {
                    cleanupSelectionDetailsText.Text = string.Format(
                        "{0} items selected. {1} blocked.",
                        selectedEntries.Count,
                        blockedSelectedCount);
                }
            }

            if (cleanupActionButton != null)
            {
                cleanupActionButton.IsEnabled = hasScanRun && selectedEntries.Count > 0 && !operationInProgress;
            }

            if (cleanupTargetPanel != null)
            {
                cleanupTargetPanel.Children.Clear();

                foreach (string key in CleanupTargetOrder)
                {
                    var snapshot = BuildCleanupTargetSnapshot(key);
                    cleanupTargetPanel.Children.Add(BuildCleanupTargetTile(snapshot));
                }
            }

            if (cleanupBreakdownTitleText != null)
            {
                cleanupBreakdownTitleText.Text = !hasScanRun
                    ? "Breakdown"
                    : selectedEntries.Count == 0
                        ? "Available by target"
                        : "Selected target breakdown";
            }

            if (cleanupBreakdownPanel != null)
            {
                cleanupBreakdownPanel.Children.Clear();

                if (!hasScanRun)
                {
                    cleanupBreakdownPanel.Children.Add(BuildEmptyState("Run a scan to populate the breakdown."));
                }
                else if (totalCleanableCount == 0)
                {
                    cleanupBreakdownPanel.Children.Add(BuildEmptyState("No cleanable items were found in the supported locations."));
                }
                else
                {
                    var keysToShow = selectedEntries.Count == 0
                        ? CleanupTargetOrder.ToList()
                        : selectedKeys;

                    foreach (string key in keysToShow)
                    {
                        var snapshot = BuildCleanupTargetSnapshot(key);
                        if (!snapshot.IsEnabled && hasScanRun)
                        {
                            continue;
                        }

                        cleanupBreakdownPanel.Children.Add(BuildBreakdownTile(snapshot, selectedEntries.Count == 0 ? totalCleanableBytes : Math.Max(1, selectedBytes)));
                    }
                }
            }

            if (cleanupPreviewGrid != null && cleanupPreviewCaptionText != null)
            {
                var previewEntries = GetPreviewEntries(selectedEntries);
                cleanupPreviewGrid.ItemsSource = previewEntries;

                cleanupPreviewCaptionText.Text = !hasScanRun
                    ? "A list will appear here after the scan."
                    : selectedEntries.Count == 0
                        ? "Largest matches from the last scan."
                        : "Files from the current selection.";
            }

            UpdateHeaderState();
            UpdateSidebarState();
            UpdateStatusBarState();
        }

        private UIElement BuildCleanupTargetTile(CleanupTargetSnapshot snapshot)
        {
            var border = new Border
            {
                Width = 278,
                MinHeight = 186,
                Margin = new Thickness(6),
                Padding = new Thickness(16),
                Background = snapshot.IsSelected ? SelectedTileBackgroundBrush() : SurfaceMutedBrush(),
                BorderBrush = snapshot.IsSelected ? AccentBrush() : OutlineBrush(),
                BorderThickness = new Thickness(snapshot.IsSelected ? 2 : 1),
                CornerRadius = new CornerRadius(22),
                Opacity = snapshot.IsEnabled || !hasScanRun ? 1.0 : 0.58
            };

            if (snapshot.IsEnabled)
            {
                border.Cursor = Cursors.Hand;
                var targetKey = snapshot.Key;
                border.MouseLeftButtonUp += delegate { ToggleCleanupTargetSelection(targetKey); };
            }

            var stack = new StackPanel();
            border.Child = stack;

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            header.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            header.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            stack.Children.Add(header);

            var icon = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = snapshot.IsSelected ? AccentBrush() : AccentMutedBrush(),
                BorderBrush = snapshot.IsSelected ? AccentBrush() : AccentOutlineBrush(),
                BorderThickness = new Thickness(1)
            };
            icon.Child = new TextBlock
            {
                Text = GetCleanupTargetIconText(snapshot.Key),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = snapshot.IsSelected ? OnAccentTextBrush() : AccentBrush()
            };
            Grid.SetColumn(icon, 0);
            header.Children.Add(icon);

            var titleStack = new StackPanel
            {
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(titleStack, 1);
            header.Children.Add(titleStack);

            titleStack.Children.Add(new TextBlock
            {
                Text = snapshot.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = PrimaryTextBrush()
            });

            titleStack.Children.Add(new TextBlock
            {
                Text = hasScanRun
                    ? snapshot.IsEnabled
                        ? string.Format("{0} items / {1}", snapshot.CleanableCount, SizeFormatter.Format(snapshot.CleanableBytes))
                        : "No results"
                        : "Available after the scan",
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 11,
                Foreground = AccentBrush()
            });

            var badge = new Border
            {
                Background = snapshot.IsSelected ? AccentBrush() : OutlineBrush(),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Top
            };
            badge.Child = new TextBlock
            {
                Text = snapshot.IsSelected
                    ? "Selected"
                    : snapshot.IsEnabled
                        ? "Ready"
                        : "Locked",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = snapshot.IsSelected ? OnAccentTextBrush() : SecondaryTextBrush()
            };
            Grid.SetColumn(badge, 2);
            header.Children.Add(badge);

            stack.Children.Add(new TextBlock
            {
                Text = snapshot.Description,
                Margin = new Thickness(0, 14, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush()
            });

            stack.Children.Add(new TextBlock
            {
                Text = !hasScanRun
                    ? "Run a scan first."
                    : snapshot.BlockedCount > 0
                        ? string.Format("{0} blocked.", snapshot.BlockedCount)
                        : "No blocked files.",
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = snapshot.BlockedCount > 0 ? WarningBrush() : SecondaryTextBrush()
            });

            return border;
        }

        private UIElement BuildBreakdownTile(CleanupTargetSnapshot snapshot, long referenceBytes)
        {
            double progressValue = referenceBytes <= 0
                ? 0
                : (snapshot.CleanableBytes * 100.0) / referenceBytes;

            if (progressValue < 0)
            {
                progressValue = 0;
            }

            if (progressValue > 100)
            {
                progressValue = 100;
            }

            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(14),
                Background = snapshot.IsSelected ? SelectedTileBackgroundBrush() : SurfaceMutedBrush(),
                BorderBrush = snapshot.IsSelected ? AccentBrush() : OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18)
            };

            var stack = new StackPanel();
            border.Child = stack;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            stack.Children.Add(row);

            row.Children.Add(new TextBlock
            {
                Text = snapshot.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush()
            });

            var value = new TextBlock
            {
                Text = SizeFormatter.Format(snapshot.CleanableBytes),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush()
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            var progress = new ProgressBar
            {
                Height = 9,
                Minimum = 0,
                Maximum = 100,
                Value = progressValue,
                Margin = new Thickness(0, 10, 0, 0)
            };
            stack.Children.Add(progress);

            stack.Children.Add(new TextBlock
            {
                Text = string.Format(
                    "{0} cleanable item(s){1}",
                    snapshot.CleanableCount,
                    snapshot.BlockedCount > 0 ? string.Format(" | {0} blocked", snapshot.BlockedCount) : string.Empty),
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11,
                Foreground = SecondaryTextBrush()
            });

            return border;
        }

        private CleanupTargetSnapshot BuildCleanupTargetSnapshot(string key)
        {
            var entries = GetTargetEntries(key).ToList();

            return new CleanupTargetSnapshot
            {
                Key = key,
                Title = GetCleanupTargetTitle(key),
                Description = GetCleanupTargetDescription(key),
                CleanableCount = entries.Count(item => item.IsCleanable),
                CleanableBytes = entries.Where(item => item.IsCleanable).Sum(item => item.SizeBytes),
                BlockedCount = entries.Count(item => !item.IsCleanable),
                IsSelected = selectedCleanupTargets.Contains(key),
                IsEnabled = hasScanRun && entries.Any(item => item.IsCleanable)
            };
        }

        private List<ScanEntry> GetPreviewEntries(List<ScanEntry> selectedEntries)
        {
            if (!hasScanRun)
            {
                return new List<ScanEntry>();
            }

            IEnumerable<ScanEntry> source = selectedEntries.Count > 0
                ? selectedEntries
                : allEntries.Where(item => item.IsCleanable);

            return source
                .OrderByDescending(item => item.SizeBytes)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Take(120)
                .ToList();
        }

        private void SwitchSection(NavigationSection section)
        {
            if (operationInProgress || startupReloadInProgress || startupToggleInProgress || activeSection == section)
            {
                return;
            }

            activeSection = section;
            if (!operationInProgress)
            {
                if (activeSection == NavigationSection.Apps)
                {
                    currentStatusMessage = "Uninstall apps is open.";
                }
                else if (activeSection == NavigationSection.Startup)
                {
                    currentStatusMessage = "Startup is open.";
                }
                else
                {
                    currentStatusMessage = hasScanRun
                        ? "Choose the areas for cleanup now."
                        : "Ready. Start the first scan now.";
                }
            }
            RenderCurrentSection();
        }

        private void UpdateNavigationState()
        {
            foreach (var pair in navigationButtons)
            {
                ApplyNavigationButtonStyle(pair.Value, pair.Key == activeSection);
                pair.Value.IsEnabled = !operationInProgress && !startupReloadInProgress && !startupToggleInProgress;
            }
        }

        private void ApplyNavigationButtonStyle(Button button, bool isSelected)
        {
            if (button == null)
            {
                return;
            }

            button.Background = isSelected ? AccentMutedBrush() : Brushes.Transparent;
            button.BorderBrush = isSelected ? AccentOutlineBrush() : OutlineBrush();
            button.Foreground = isSelected ? AccentBrush() : PrimaryTextBrush();
        }

        private void UpdateHeaderState()
        {
            bool isAppsSection = activeSection == NavigationSection.Apps;
            bool isStartupSection = activeSection == NavigationSection.Startup;
            var selectedEntries = GetSelectedEntries().ToList();
            long selectedBytes = selectedEntries.Sum(item => item.SizeBytes);
            int enabledStartupCount = allStartupItems.Count(item => item.IsEnabled);
            int disabledStartupCount = allStartupItems.Count - enabledStartupCount;

            if (headerEyebrowText != null)
            {
                headerEyebrowText.Text = isAppsSection
                    ? "Programs"
                    : isStartupSection
                        ? "Windows startup"
                        : "Simple flow";
            }

            if (headerDeviceText != null)
            {
                headerDeviceText.Text = isAppsSection
                    ? "Uninstall apps"
                    : isStartupSection
                        ? "Startup"
                        : "Clean up";
            }

            if (headerMetaText != null)
            {
                headerMetaText.Text = isAppsSection
                    ? "Here you can see the programs registered in Windows."
                    : isStartupSection
                        ? "Manage enabled and disabled startup entries here."
                        : "1. Start scan   ->   2. Choose areas   ->   3. Start cleanup";
            }

            if (headerScanStatusText != null)
            {
                if (isAppsSection)
                {
                    headerScanStatusText.Text = "Choose an app and click \"Uninstall\" to open its uninstaller.";
                }
                else if (isStartupSection)
                {
                    headerScanStatusText.Text = "Check or clear the box to enable or disable startup directly.";
                }
                else if (!hasScanRun)
                {
                    headerScanStatusText.Text = "Start with step 1 and click \"1. Start scan\".";
                }
                else if (currentSnapshot == null || currentSnapshot.CleanableCount == 0)
                {
                    headerScanStatusText.Text = "The scan is complete, but no cleanable files were found.";
                }
                else
                {
                    headerScanStatusText.Text = selectedEntries.Count == 0
                        ? "Step 2: Choose the areas that should be cleaned."
                        : "Step 3: Start cleanup with the current selection.";
                }
            }

            if (headerRightLabelText != null)
            {
                headerRightLabelText.Text = isAppsSection || isStartupSection ? "Entries" : "Next step";
            }

            if (headerSelectionStatusText != null)
            {
                if (isAppsSection)
                {
                    headerSelectionStatusText.Text = visibleApps.Count.ToString();
                }
                else if (isStartupSection)
                {
                    headerSelectionStatusText.Text = visibleStartupItems.Count.ToString();
                }
                else
                {
                    headerSelectionStatusText.Text = !hasScanRun
                        ? "Step 1"
                        : selectedEntries.Count == 0
                            ? "Step 2"
                            : "Step 3";
                }
            }

            if (headerLastActionText != null)
            {
                if (isAppsSection)
                {
                    headerLastActionText.Text = string.Format(
                        "{0} visible of {1}. Use search, filters, or the \"Uninstall\" button.",
                        visibleApps.Count,
                        allInstalledApps.Count);
                }
                else if (isStartupSection)
                {
                    headerLastActionText.Text = string.Format(
                        "{0} enabled, {1} disabled. Use search, filters, or the checkbox directly in the list.",
                        enabledStartupCount,
                        disabledStartupCount);
                }
                else
                {
                    headerLastActionText.Text = !hasScanRun
                        ? "No scan yet started."
                        : selectedEntries.Count == 0
                            ? "Nothing selected yet."
                            : string.Format("{0} items / {1} ready.", selectedEntries.Count, SizeFormatter.Format(selectedBytes));
                }
            }

            if (headerScanButton != null)
            {
                headerScanButton.Visibility = isAppsSection || isStartupSection ? Visibility.Collapsed : Visibility.Visible;
                headerScanButton.IsEnabled = !operationInProgress;
                headerScanButton.Content = hasScanRun ? "1. Rescan" : "1. Start scan";
            }

            if (headerCleanButton != null)
            {
                headerCleanButton.Visibility = isAppsSection || isStartupSection ? Visibility.Collapsed : Visibility.Visible;

                if (isAppsSection || isStartupSection)
                {
                    headerCleanButton.IsEnabled = false;
                }
                else if (!hasScanRun)
                {
                    headerCleanButton.Content = "2. Choose areas";
                    headerCleanButton.IsEnabled = false;
                }
                else if (selectedEntries.Count == 0)
                {
                    headerCleanButton.Content = "2. Select all";
                    headerCleanButton.IsEnabled = currentSnapshot != null && currentSnapshot.CleanableCount > 0 && !operationInProgress;
                }
                else
                {
                    headerCleanButton.Content = "3. Start cleanup";
                    headerCleanButton.IsEnabled = !operationInProgress;
                }
            }

            if (headerButtonGrid != null)
            {
                headerButtonGrid.Visibility = isAppsSection || isStartupSection ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void UpdateSidebarState()
        {
        }

        private void UpdateStatusBarState()
        {
            if (statusText != null)
            {
                statusText.Text = currentStatusMessage;
            }
        }

        private void HandleHeaderAction()
        {
            if (!hasScanRun)
            {
                return;
            }

            if (!GetSelectedEntries().Any())
            {
                SelectVisibleCleanableItems();
                return;
            }

            MoveSelectedItemsToRecycleBin();
        }

        private void SelectVisibleCleanableItems()
        {
            if (operationInProgress)
            {
                currentStatusMessage = "Please wait. Another action is already running.";
                UpdateStatusBarState();
                return;
            }

            var selectableTargets = CleanupTargetOrder
                .Where(delegate(string key)
                {
                    return hasScanRun && GetTargetEntries(key).Any(item => item.IsCleanable);
                })
                .ToList();

            if (selectableTargets.Count == 0)
            {
                currentStatusMessage = "No areas are available yet.";
                UpdateStatusBarState();
                return;
            }

            SelectVisibleCleanableItemsAsync(selectableTargets);
        }

        private async void SelectVisibleCleanableItemsAsync(List<string> selectableTargets)
        {
            try
            {
                BeginOperation("Selecting areas...", "Selecting all areas...", false, 0, selectableTargets.Count);

                int index = 0;
                foreach (string key in selectableTargets)
                {
                    selectedCleanupTargets.Add(key);
                    index++;

                    UpdateOperationProgress(new OperationProgressInfo
                    {
                        Message = "Selecting all areas...",
                        Current = index,
                        Total = selectableTargets.Count,
                        IsIndeterminate = false
                    });

                    await Task.Delay(1);
                }

                lastActionMessage = "All available areas were selected.";
                EndOperation("All areas were selected.");
                UpdateCleanupView();
            }
            catch (Exception ex)
            {
                EndOperation("Selection failed: " + ex.Message);
            }
        }

        private void ClearSelection()
        {
            selectedCleanupTargets.Clear();
            lastActionMessage = "Selection cleared.";
            currentStatusMessage = "Selection cleared.";

            if (activeSection == NavigationSection.Cleanup)
            {
                UpdateCleanupView();
            }
            else
            {
                UpdateHeaderState();
                UpdateSidebarState();
                UpdateStatusBarState();
            }
        }

        private void ToggleCleanupTargetSelection(string key)
        {
            if (!hasScanRun || !GetTargetEntries(key).Any(item => item.IsCleanable))
            {
                return;
            }

            if (!selectedCleanupTargets.Add(key))
            {
                selectedCleanupTargets.Remove(key);
            }

            currentStatusMessage = "Selection updated.";
            if (activeSection == NavigationSection.Cleanup)
            {
                UpdateCleanupView();
            }
            else
            {
                UpdateHeaderState();
                UpdateSidebarState();
                UpdateStatusBarState();
            }
        }

        private async void MoveSelectedItemsToRecycleBin()
        {
            if (operationInProgress)
            {
                currentStatusMessage = "Please wait. Another action is already running.";
                UpdateStatusBarState();
                return;
            }

            var selectedEntries = GetSelectedEntries().ToList();
            if (selectedEntries.Count == 0)
            {
                currentStatusMessage = "Choose at least one area first.";
                UpdateStatusBarState();
                return;
            }

            long totalBytes = selectedEntries.Sum(item => item.SizeBytes);
            var browserProcesses = BrowserProcessService.GetRequiredBrowserProcesses(selectedEntries);
            bool containsBrowserCache = browserProcesses.Count > 0;

            string confirmMessage;
            if (containsBrowserCache)
            {
                string browserLabels = BrowserProcessService.BuildBrowserLabelSummary(browserProcesses);
                confirmMessage = string.Format(
                    "Browser cache can only be cleaned if the browser is closed.\n\nPress OK to close these browsers automatically: {0}\n\nSelected items: {1}\nSelected size: {2}\n\nPlease save any open browser data first.",
                    browserLabels,
                    selectedEntries.Count,
                    SizeFormatter.Format(totalBytes));
            }
            else
            {
                confirmMessage = string.Format(
                    "Move {0} selected items to the Recycle Bin?\n\nSelected size: {1}\n\nOnly temporary files will be cleaned.",
                    selectedEntries.Count,
                    SizeFormatter.Format(totalBytes));
            }

            var confirm = MessageBox.Show(
                confirmMessage,
                containsBrowserCache ? "Close browsers and continue" : "Confirm cleanup",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.OK)
            {
                currentStatusMessage = "Cleanup cancelled.";
                UpdateStatusBarState();
                return;
            }

            if (containsBrowserCache)
            {
                BeginOperation("Preparing browsers...", "Closing browsers...", true, 0, 0);

                var browserCloseResult = await Task.Run(delegate
                {
                    return browserProcessService.CloseBrowsers(browserProcesses);
                });

                if (browserCloseResult.FailedProcessCount > 0)
                {
                    EndOperation("Cleanup was cancelled because a browser could not be closed.");
                    MessageBox.Show(
                        "Cleanup was cancelled because not all required browsers could be closed.\n\n" + browserCloseResult.Summary,
                        "Could not close browser",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (browserCloseResult.AttemptedProcessCount > 0)
                {
                    lastActionMessage = browserCloseResult.Summary;
                }
            }

            BeginOperation(
                "Moving files to the Recycle Bin...",
                "Cleanup is starting...",
                false,
                0,
                selectedEntries.Count);

            var progress = new Progress<OperationProgressInfo>(UpdateOperationProgress);
            var result = await Task.Run(delegate
            {
                return deletionService.MoveToRecycleBin(selectedEntries, progress);
            });

            lastActionMessage = string.Format(
                "Moved {0} items to the Recycle Bin. Freed {1}.",
                result.MovedCount,
                SizeFormatter.Format(result.MovedBytes));

            EndOperation(string.Format(
                "Cleanup finished. Moved {0}, skipped {1}, failed {2}.",
                result.MovedCount,
                result.SkippedCount,
                result.FailedCount));

            MessageBox.Show(
                string.Format(
                    "Moved: {0}\nSkipped: {1}\nFailed: {2}\nFreed: {3}\n\nLog file: {4}",
                    result.MovedCount,
                    result.SkippedCount,
                    result.FailedCount,
                    SizeFormatter.Format(result.MovedBytes),
                    result.LogPath),
                "Cleanup result",
                MessageBoxButton.OK,
                result.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            await RefreshScanOnlyInternalAsync();
        }

        private void ExportCsv()
        {
            try
            {
                List<ScanEntry> exportEntries = currentEntries.Count > 0
                    ? currentEntries
                    : new List<ScanEntry>(allEntries);

                string exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
                Directory.CreateDirectory(exportDir);

            string fileName = "sweepcore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
                string exportPath = Path.Combine(exportDir, fileName);
                var builder = new StringBuilder();
                builder.AppendLine("Section,Category,Risk,SizeBytes,LastWriteTime,Path,Reason");

                foreach (var entry in exportEntries)
                {
                    builder.AppendLine(string.Join(",",
                        Escape(entry.Section),
                        Escape(entry.Category),
                        Escape(entry.RiskDisplay),
                        entry.SizeBytes.ToString(),
                        Escape(entry.LastWriteTime.ToString("o")),
                        Escape(entry.Path),
                        Escape(entry.Reason)));
                }

                File.WriteAllText(exportPath, builder.ToString(), Encoding.UTF8);
                lastActionMessage = "CSV exportiert nach " + exportPath;
                currentStatusMessage = "CSV erfolgreich exportiert.";
                UpdateSidebarState();
                UpdateStatusBarState();
            }
            catch (Exception ex)
            {
                currentStatusMessage = "CSV export failed: " + ex.Message;
                UpdateStatusBarState();
            }
        }

        private IEnumerable<ScanEntry> GetSelectedEntries()
        {
            var selected = new List<ScanEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string key in GetSelectedTargetKeys())
            {
                foreach (var entry in GetTargetEntries(key))
                {
                    if (!entry.IsCleanable)
                    {
                        continue;
                    }

                    if (seenPaths.Add(entry.Path))
                    {
                        selected.Add(entry);
                    }
                }
            }

            return selected;
        }

        private List<string> GetSelectedTargetKeys()
        {
            return CleanupTargetOrder
                .Where(item => selectedCleanupTargets.Contains(item))
                .ToList();
        }

        private IEnumerable<ScanEntry> GetTargetEntries(string key)
        {
            if (string.Equals(key, TargetTemp, StringComparison.OrdinalIgnoreCase))
            {
                return allEntries.Where(item => string.Equals(item.Section, "Temporary Files", StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(key, TargetChrome, StringComparison.OrdinalIgnoreCase))
            {
                return allEntries.Where(item =>
                    string.Equals(item.Section, "Browser Cache", StringComparison.OrdinalIgnoreCase) &&
                    item.Category.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(key, TargetEdge, StringComparison.OrdinalIgnoreCase))
            {
                return allEntries.Where(item =>
                    string.Equals(item.Section, "Browser Cache", StringComparison.OrdinalIgnoreCase) &&
                    item.Category.StartsWith("Edge", StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(key, TargetBrave, StringComparison.OrdinalIgnoreCase))
            {
                return allEntries.Where(item =>
                    string.Equals(item.Section, "Browser Cache", StringComparison.OrdinalIgnoreCase) &&
                    item.Category.StartsWith("Brave", StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(key, TargetFirefox, StringComparison.OrdinalIgnoreCase))
            {
                return allEntries.Where(item =>
                    string.Equals(item.Section, "Browser Cache", StringComparison.OrdinalIgnoreCase) &&
                    item.Category.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase));
            }

            return new List<ScanEntry>();
        }

        private static string GetCleanupTargetIconText(string key)
        {
            if (string.Equals(key, TargetTemp, StringComparison.OrdinalIgnoreCase))
            {
                return "SYS";
            }

            if (string.Equals(key, TargetChrome, StringComparison.OrdinalIgnoreCase))
            {
                return "CH";
            }

            if (string.Equals(key, TargetEdge, StringComparison.OrdinalIgnoreCase))
            {
                return "ED";
            }

            if (string.Equals(key, TargetBrave, StringComparison.OrdinalIgnoreCase))
            {
                return "BR";
            }

            if (string.Equals(key, TargetFirefox, StringComparison.OrdinalIgnoreCase))
            {
                return "FF";
            }

            return "--";
        }

        private static string GetCleanupTargetTitle(string key)
        {
            if (string.Equals(key, TargetTemp, StringComparison.OrdinalIgnoreCase))
            {
                return "Temporary files";
            }

            if (string.Equals(key, TargetChrome, StringComparison.OrdinalIgnoreCase))
            {
                return "Google Chrome";
            }

            if (string.Equals(key, TargetEdge, StringComparison.OrdinalIgnoreCase))
            {
                return "Microsoft Edge";
            }

            if (string.Equals(key, TargetBrave, StringComparison.OrdinalIgnoreCase))
            {
                return "Brave";
            }

            if (string.Equals(key, TargetFirefox, StringComparison.OrdinalIgnoreCase))
            {
                return "Mozilla Firefox";
            }

            return key;
        }

        private static string GetCleanupTargetDescription(string key)
        {
            if (string.Equals(key, TargetTemp, StringComparison.OrdinalIgnoreCase))
            {
                return "User temp, Windows temp, crash dumps, and Windows error reports.";
            }

            return "Cache only. Passwords, saved addresses, forms, and profile data stay untouched.";
        }

        private UIElement BuildLogoVisual(double size)
        {
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "sweepcore-hero-logo.png");
            if (File.Exists(logoPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    return new Image
                    {
                        Width = size,
                        Height = size,
                        Stretch = Stretch.Uniform,
                        Source = bitmap
                    };
                }
                catch
                {
                }
            }

            var fallback = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 4),
                Background = AccentBrush(),
                BorderBrush = AccentBorderBrush(),
                BorderThickness = new Thickness(1)
            };

            fallback.Child = new TextBlock
            {
                Text = "TC",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = HeadingFontFamily(),
                FontSize = size * 0.28,
                FontWeight = FontWeights.SemiBold,
                Foreground = OnAccentTextBrush()
            };

            return fallback;
        }

        private FontFamily HeadingFontFamily()
        {
            return new FontFamily("Bahnschrift");
        }

        private void ResetPageReferences()
        {
            appSearchBox = null;
            appsFilterComboBox = null;
            appsSortComboBox = null;
            appsTilePanel = null;
            appsSummaryText = null;
            startupSearchBox = null;
            startupFilterComboBox = null;
            startupItemPanel = null;
            startupSummaryText = null;
            cleanupTargetPanel = null;
            cleanerSummaryText = null;
            selectionModeText = null;
            selectionSummaryText = null;
            cleanupSelectionDetailsText = null;
            cleanupBreakdownTitleText = null;
            cleanupActionHintText = null;
            cleanupBreakdownPanel = null;
            cleanupActionButton = null;
            cleanupPreviewGrid = null;
            cleanupPreviewCaptionText = null;
        }

        private void BeginOperation(string statusMessage, string progressMessage, bool isIndeterminate, int current, int total)
        {
            operationInProgress = true;
            currentStatusMessage = statusMessage;

            if (cleanupActionButton != null)
            {
                cleanupActionButton.IsEnabled = false;
            }

            UpdateNavigationState();
            UpdateHeaderState();
            UpdateStatusBarState();

            UpdateOperationProgress(new OperationProgressInfo
            {
                Message = progressMessage,
                IsIndeterminate = isIndeterminate,
                Current = current,
                Total = total
            });

            Mouse.OverrideCursor = Cursors.Wait;
        }

        private void UpdateOperationProgress(OperationProgressInfo info)
        {
            if (info == null || operationProgressBorder == null || operationProgressBar == null || operationText == null)
            {
                return;
            }

            operationProgressBorder.Visibility = Visibility.Visible;
            operationText.Text = string.IsNullOrWhiteSpace(info.Message) ? "Working..." : info.Message;
            operationProgressBar.IsIndeterminate = info.IsIndeterminate;

            if (!info.IsIndeterminate)
            {
                int safeTotal = info.Total <= 0 ? 1 : info.Total;
                int safeCurrent = info.Current < 0 ? 0 : info.Current;
                if (safeCurrent > safeTotal)
                {
                    safeCurrent = safeTotal;
                }

                operationProgressBar.Minimum = 0;
                operationProgressBar.Maximum = safeTotal;
                operationProgressBar.Value = safeCurrent;
            }
            else
            {
                operationProgressBar.Value = 0;
            }
        }

        private void EndOperation(string statusMessage)
        {
            operationInProgress = false;
            currentStatusMessage = statusMessage;
            Mouse.OverrideCursor = null;

            if (operationProgressBorder != null)
            {
                operationProgressBorder.Visibility = Visibility.Collapsed;
            }

            if (operationProgressBar != null)
            {
                operationProgressBar.IsIndeterminate = false;
                operationProgressBar.Value = 0;
            }

            if (operationText != null)
            {
                operationText.Text = string.Empty;
            }

            UpdateNavigationState();
            UpdateHeaderState();
            UpdateSidebarState();
            UpdateStatusBarState();
        }

        private UIElement BuildStatusBar()
        {
            var border = new Border
            {
                Background = SurfaceBrush(),
                BorderBrush = OutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 18, 0, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            border.Child = grid;

            statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush(),
                Text = currentStatusMessage
            };
            Grid.SetColumn(statusText, 0);
            grid.Children.Add(statusText);

            operationProgressBorder = new Border
            {
                Visibility = Visibility.Collapsed,
                Background = SurfaceMutedBrush(),
                BorderBrush = AccentOutlineBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(18, 0, 0, 0)
            };
            Grid.SetColumn(operationProgressBorder, 1);
            grid.Children.Add(operationProgressBorder);

            var progressStack = new StackPanel
            {
                Width = 340
            };
            operationProgressBorder.Child = progressStack;

            operationText = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
                Foreground = AccentBrush()
            };
            progressStack.Children.Add(operationText);

            operationProgressBar = new ProgressBar
            {
                Height = 14,
                Minimum = 0,
                Maximum = 1
            };
            progressStack.Children.Add(operationProgressBar);

            return border;
        }

        private Brush WindowBackgroundBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(8, 13, 22) : Color.FromRgb(244, 247, 251));
        }

        private Brush SidebarBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(12, 18, 30) : Color.FromRgb(248, 250, 253));
        }

        private Brush SurfaceBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(18, 27, 43) : Color.FromRgb(255, 255, 255));
        }

        private Brush SurfaceMutedBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(13, 21, 35) : Color.FromRgb(247, 249, 252));
        }

        private Brush OutlineBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(48, 66, 90) : Color.FromRgb(219, 227, 236));
        }

        private Brush PrimaryTextBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(232, 237, 243) : Color.FromRgb(20, 31, 46));
        }

        private Brush SecondaryTextBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(145, 164, 188) : Color.FromRgb(92, 107, 126));
        }

        private Brush AccentBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(110, 189, 255) : Color.FromRgb(16, 110, 240));
        }

        private Brush AccentBorderBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(84, 170, 246) : Color.FromRgb(13, 92, 214));
        }

        private Brush AccentOutlineBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(52, 111, 163) : Color.FromRgb(185, 216, 255));
        }

        private Brush AccentMutedBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(20, 46, 73) : Color.FromRgb(230, 241, 255));
        }

        private Brush SelectedTileBackgroundBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(16, 39, 64) : Color.FromRgb(240, 247, 255));
        }

        private Brush OnAccentTextBrush()
        {
            return CreateBrush(Color.FromRgb(255, 255, 255));
        }

        private Brush WarningBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(251, 191, 36) : Color.FromRgb(180, 83, 9));
        }

        private Brush HeroBackgroundBrush()
        {
            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            if (isDarkMode)
            {
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(12, 54, 104), 0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(10, 102, 184), 1));
            }
            else
            {
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(20, 94, 202), 0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(50, 155, 248), 1));
            }

            return brush;
        }

        private Brush HeroOutlineBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromArgb(110, 167, 215, 255) : Color.FromArgb(90, 255, 255, 255));
        }

        private Brush HeroCardBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromArgb(42, 255, 255, 255) : Color.FromArgb(48, 255, 255, 255));
        }

        private Brush HeroButtonBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 255, 255, 255));
        }

        private Brush HeroChipBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(34, 255, 255, 255));
        }

        private Brush HeroPrimaryTextBrush()
        {
            return CreateBrush(Color.FromRgb(255, 255, 255));
        }

        private Brush HeroSecondaryTextBrush()
        {
            return CreateBrush(isDarkMode ? Color.FromRgb(218, 235, 255) : Color.FromRgb(230, 242, 255));
        }

        private static SolidColorBrush CreateBrush(Color color)
        {
            return new SolidColorBrush(color);
        }

        private static bool ContainsIgnoreCase(string source, string query)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            return source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}




