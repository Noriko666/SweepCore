namespace SweepCoreApp
{
    internal static class SizeFormatter
    {
        public static string Format(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes < 0 ? 0 : bytes;
            int order = 0;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return size.ToString("0.##") + " " + suffixes[order];
        }
    }
}

