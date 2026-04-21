namespace SweepCoreApp
{
    internal sealed class CandidateRule
    {
        public string Section { get; set; }
        public string Category { get; set; }
        public string RootPath { get; set; }
        public string SearchPattern { get; set; }
        public bool Recursive { get; set; }
        public int MinAgeDays { get; set; }
        public ScanRisk Risk { get; set; }
        public string Reason { get; set; }
        public long MinSizeBytes { get; set; }
    }
}

