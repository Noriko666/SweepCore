using Microsoft.Win32;

namespace SweepCoreApp
{
    internal sealed class StartupItemInfo
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string Scope { get; set; }
        public string SourceKind { get; set; }
        public string Location { get; set; }
        public bool IsEnabled { get; set; }
        public RegistryHive Hive { get; set; }
        public RegistryView View { get; set; }
        public string ToggleRegistryPath { get; set; }
        public string ToggleRegistryValueName { get; set; }
        public bool ToggleUsesStateDword { get; set; }
    }
}

