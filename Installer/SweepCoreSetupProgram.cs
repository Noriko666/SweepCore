using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SweepCoreSetup
{
    internal static class SweepCoreSetupProgram
    {
        private const string AppName = "SweepCore";
        private const string AppVersion = "1.1";
        private const string Publisher = "SweepCore";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SweepCore";
        private const string AppResourceName = "SweepCore.Payload.SweepCore.exe";
        private const string LogoResourceName = "SweepCore.Payload.sweepcore-hero-logo.png";
        private const string IconResourceName = "SweepCore.Payload.sweepcore.ico";

        private static readonly string InstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            AppName);

        private static readonly string InstallExePath = Path.Combine(InstallRoot, "SweepCore.exe");
        private static readonly string AssetsDirectory = Path.Combine(InstallRoot, "Assets");
        private static readonly string InstalledLogoPath = Path.Combine(AssetsDirectory, "sweepcore-hero-logo.png");
        private static readonly string InstalledIconPath = Path.Combine(AssetsDirectory, "sweepcore.ico");
        private static readonly string InstallerDirectory = Path.Combine(InstallRoot, "Installer");
        private static readonly string InstalledSetupPath = Path.Combine(InstallerDirectory, "SweepCoreSetup.exe");
        private static readonly string InstalledUninstallerPath = Path.Combine(InstallRoot, "Uninstall SweepCore.exe");
        private static readonly string DesktopShortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            AppName + ".lnk");

        private static readonly string StartMenuDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            AppName);

        private static readonly string StartMenuAppShortcutPath = Path.Combine(StartMenuDirectory, AppName + ".lnk");
        private static readonly string StartMenuUninstallShortcutPath = Path.Combine(StartMenuDirectory, "Uninstall " + AppName + ".lnk");

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                string currentExecutableName = Path.GetFileName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                bool launchedAsUninstaller = currentExecutableName.StartsWith("Uninstall ", StringComparison.OrdinalIgnoreCase);
                bool uninstallMode = args != null && Array.Exists(
                    args,
                    item => string.Equals(item, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item, "--uninstall", StringComparison.OrdinalIgnoreCase));
                uninstallMode = uninstallMode || launchedAsUninstaller;
                bool quietMode = args != null && Array.Exists(
                    args,
                    item => string.Equals(item, "/quiet", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item, "/silent", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item, "--quiet", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item, "--silent", StringComparison.OrdinalIgnoreCase));

                if (uninstallMode)
                {
                    UninstallInteractive(quietMode);
                }
                else
                {
                    InstallInteractive(quietMode);
                }
            }
            catch (Exception ex)
            {
                if (args != null && Array.Exists(
                    args,
                    item => string.Equals(item, "/quiet", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item, "/silent", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item, "--quiet", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item, "--silent", StringComparison.OrdinalIgnoreCase)))
                {
                    Environment.ExitCode = 1;
                    return;
                }

                MessageBox.Show(
                    ex.Message,
                    AppName + " Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void InstallInteractive(bool quietMode)
        {
            if (IsInstalled())
            {
                if (!quietMode)
                {
                    var updateChoice = MessageBox.Show(
                        AppName + " is already installed.\n\nDo you want to update the existing installation?",
                        AppName + " Setup",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (updateChoice != DialogResult.Yes)
                    {
                        return;
                    }
                }
            }

            EnsureSweepCoreIsClosed();

            Directory.CreateDirectory(InstallRoot);
            Directory.CreateDirectory(AssetsDirectory);
            Directory.CreateDirectory(InstallerDirectory);

            ExtractResourceToFile(AppResourceName, InstallExePath);
            ExtractResourceToFile(LogoResourceName, InstalledLogoPath);
            ExtractResourceToFile(IconResourceName, InstalledIconPath);
            CopyCurrentSetupExecutable();
            CopyCurrentExecutableTo(InstalledUninstallerPath);

            CreateShortcut(
                DesktopShortcutPath,
                InstallExePath,
                string.Empty,
                AppName,
                InstallExePath);

            Directory.CreateDirectory(StartMenuDirectory);
            CreateShortcut(
                StartMenuAppShortcutPath,
                InstallExePath,
                string.Empty,
                AppName,
                InstallExePath);

            CreateShortcut(
                StartMenuUninstallShortcutPath,
                InstalledUninstallerPath,
                string.Empty,
                "Uninstall " + AppName,
                InstalledUninstallerPath);

            WriteUninstallRegistration();

            if (quietMode)
            {
                return;
            }

            var launchChoice = MessageBox.Show(
                AppName + " has been installed successfully.\n\nA desktop shortcut was created.\n\nDo you want to launch it now?",
                AppName + " Setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (launchChoice == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = InstallExePath,
                    UseShellExecute = true,
                    WorkingDirectory = InstallRoot
                });
            }
        }

        private static void UninstallInteractive(bool quietMode)
        {
            if (!Directory.Exists(InstallRoot))
            {
                if (!quietMode)
                {
                    MessageBox.Show(
                        AppName + " is not currently installed for this user.",
                        AppName + " Setup",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            EnsureSweepCoreIsClosed();

            if (!quietMode)
            {
                var choice = MessageBox.Show(
                    "Remove " + AppName + " from this PC?\n\nThis will delete the installed files and remove the desktop shortcut.",
                    "Uninstall " + AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (choice != DialogResult.Yes)
                {
                    return;
                }
            }

            SafeDeleteFile(DesktopShortcutPath);
            SafeDeleteFile(StartMenuAppShortcutPath);
            SafeDeleteFile(StartMenuUninstallShortcutPath);
            SafeDeleteDirectory(StartMenuDirectory);
            DeleteUninstallRegistration();

            string cleanupScript = Path.Combine(
                Path.GetTempPath(),
                "sweepcore-uninstall-" + Guid.NewGuid().ToString("N") + ".cmd");

            string scriptContents =
                "@echo off" + Environment.NewLine +
                "ping 127.0.0.1 -n 3 > nul" + Environment.NewLine +
                "rmdir /s /q \"" + InstallRoot + "\"" + Environment.NewLine +
                "del \"%~f0\"" + Environment.NewLine;

            File.WriteAllText(cleanupScript, scriptContents, Encoding.ASCII);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + cleanupScript + "\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            if (!quietMode)
            {
                MessageBox.Show(
                    AppName + " has been removed.",
                    "Uninstall " + AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private static bool IsInstalled()
        {
            return File.Exists(InstallExePath);
        }

        private static void EnsureSweepCoreIsClosed()
        {
            foreach (Process process in Process.GetProcessesByName("SweepCore"))
            {
                try
                {
                    string processPath = process.MainModule == null ? string.Empty : process.MainModule.FileName;
                    if (!string.Equals(processPath, InstallExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        AppName + " is currently running.\n\nPlease close it and run setup again.");
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch
                {
                    // If the process path cannot be read, do not block the install.
                }
            }
        }

        private static void CopyCurrentSetupExecutable()
        {
            CopyCurrentExecutableTo(InstalledSetupPath);
        }

        private static void CopyCurrentExecutableTo(string destinationPath)
        {
            string currentExecutablePath = Assembly.GetExecutingAssembly().Location;
            if (string.Equals(currentExecutablePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(currentExecutablePath, destinationPath, true);
        }

        private static void ExtractResourceToFile(string resourceName, string destinationPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                {
                    throw new InvalidOperationException("Missing embedded resource: " + resourceName);
                }

                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    resourceStream.CopyTo(fileStream);
                }
            }
        }

        private static void CreateShortcut(
            string shortcutPath,
            string targetPath,
            string arguments,
            string description,
            string iconLocation)
        {
            string directory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("WScript.Shell is not available.");
            }

            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(targetPath) ?? InstallRoot });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconLocation });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private static void WriteUninstallRegistration()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Could not create uninstall registry entry.");
                }

                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", AppVersion);
                key.SetValue("Publisher", Publisher);
                key.SetValue("InstallLocation", InstallRoot);
                key.SetValue("DisplayIcon", InstallExePath);
                key.SetValue("UninstallString", "\"" + InstalledUninstallerPath + "\"");
                key.SetValue("QuietUninstallString", "\"" + InstalledUninstallerPath + "\" /quiet");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        private static void DeleteUninstallRegistration()
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        }

        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path) &&
                    Directory.GetFiles(path).Length == 0 &&
                    Directory.GetDirectories(path).Length == 0)
                {
                    Directory.Delete(path, false);
                }
            }
            catch
            {
            }
        }
    }
}
