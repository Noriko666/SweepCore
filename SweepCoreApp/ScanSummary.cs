namespace SweepCoreApp
{
    internal sealed class ScanSummary
    {
        public int TotalCount { get; set; }
        public int SafeCount { get; set; }
        public int ReviewCount { get; set; }
        public int ProtectedCount { get; set; }
        public long TotalBytes { get; set; }
    }
}

