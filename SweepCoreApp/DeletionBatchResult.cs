namespace SweepCoreApp
{
    internal sealed class DeletionBatchResult
    {
        public int RequestedCount { get; set; }
        public int MovedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public long RequestedBytes { get; set; }
        public long MovedBytes { get; set; }
        public string LogPath { get; set; }
    }
}

