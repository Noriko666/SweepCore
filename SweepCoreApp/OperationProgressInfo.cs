namespace SweepCoreApp
{
    internal sealed class OperationProgressInfo
    {
        public string Message { get; set; }
        public int Current { get; set; }
        public int Total { get; set; }
        public bool IsIndeterminate { get; set; }
    }
}

