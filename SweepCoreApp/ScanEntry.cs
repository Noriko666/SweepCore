using System;

namespace SweepCoreApp
{
    internal sealed class ScanEntry
    {
        public string Section { get; set; }
        public string Category { get; set; }
        public string Path { get; set; }
        public long SizeBytes { get; set; }
        public DateTime LastWriteTime { get; set; }
        public string Reason { get; set; }
        public ScanRisk Risk { get; set; }

        public string SizeDisplay
        {
            get { return SizeFormatter.Format(SizeBytes); }
        }

        public string RiskDisplay
        {
            get { return Risk.ToString(); }
        }

        public bool IsCleanable
        {
            get { return Risk == ScanRisk.Safe; }
        }
    }
}

