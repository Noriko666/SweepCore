namespace SweepCoreApp
{
    internal sealed class BrowserCloseResult
    {
        public int AttemptedProcessCount { get; set; }
        public int ClosedProcessCount { get; set; }
        public int ForcedProcessCount { get; set; }
        public int FailedProcessCount { get; set; }
        public string Summary { get; set; }
    }
}

