namespace SweepCoreApp
{
    internal sealed class SystemSnapshot
    {
        public string DeviceName { get; set; }
        public string OperatingSystem { get; set; }
        public string Processor { get; set; }
        public string MemoryDisplay { get; set; }
        public string SystemDriveLabel { get; set; }
        public string SystemDriveUsage { get; set; }
        public double SystemDriveUsagePercent { get; set; }
        public int InstalledAppCount { get; set; }
        public int MachineAppCount { get; set; }
        public int UserAppCount { get; set; }
        public int TempFileCount { get; set; }
        public long TempFileBytes { get; set; }
        public int BrowserCacheCount { get; set; }
        public long BrowserCacheBytes { get; set; }
        public int CleanableCount { get; set; }
        public long CleanableBytes { get; set; }
        public int BlockedCount { get; set; }
    }
}

