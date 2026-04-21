using System;

namespace SweepCoreApp
{
    internal sealed class InstalledAppInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Publisher { get; set; }
        public string Scope { get; set; }
        public string UninstallCommand { get; set; }
        public string DisplayIconPath { get; set; }
        public long EstimatedSizeBytes { get; set; }
        public DateTime? InstallDate { get; set; }

        public bool CanUninstall
        {
            get { return !string.IsNullOrWhiteSpace(UninstallCommand); }
        }
    }
}

