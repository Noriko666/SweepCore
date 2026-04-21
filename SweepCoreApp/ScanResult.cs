using System.Collections.Generic;

namespace SweepCoreApp
{
    internal sealed class ScanResult
    {
        public ScanResult(List<ScanEntry> entries, ScanSummary summary)
        {
            Entries = entries;
            Summary = summary;
        }

        public List<ScanEntry> Entries { get; private set; }

        public ScanSummary Summary { get; private set; }
    }
}

