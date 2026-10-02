using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>One file a mod shares with something else that is swapped in too.</summary>
    public sealed class ManagedModConflict
    {
        public string Path { get; init; } = "";

        /// <summary>What else provides this file, like "Classic sounds" or "your mods folder".</summary>
        public string With { get; init; } = "";
    }

    public sealed class ManagedModItem : NotifyPropertyChangedViewModel
    {
        public ManagedModItem(string id, string name, bool enabled, DateTime createdUtc, int fileCount, long totalBytes, int conflictCount, string scanError)
        {
            Id = id;
            Name = name;
            Enabled = enabled;
            CreatedUtc = createdUtc;
            FileCount = fileCount;
            TotalBytes = totalBytes;
            ConflictCount = conflictCount;
            ScanError = scanError;
        }

        public string Id { get; }

        public string ShortId => Id.Length > 8 ? Id[..8] : Id;

        public string Name { get; }

        public DateTime CreatedUtc { get; }

        public int FileCount { get; }

        public long TotalBytes { get; }

        public int ConflictCount { get; }

        public bool HasConflicts => ConflictCount > 0;

        public string ScanError { get; }

        public bool HasScanError => !string.IsNullOrEmpty(ScanError);

        public bool Enabled { get; }

        public string FileSummary => FileCount + (FileCount == 1 ? " file, " : " files, ") + FormatBytes(TotalBytes);

        /// <summary>Every file this mod shares with another active mod or the mods folder, worked out when the list loads.</summary>
        public IReadOnlyList<ManagedModConflict> Conflicts { get; init; } = Array.Empty<ManagedModConflict>();

        public string ConflictText
        {
            get
            {
                if (Conflicts.Count > 0)
                {
                    ManagedModConflict first = Conflicts[0];
                    string more = Conflicts.Count > 1 ? $" and {Conflicts.Count - 1} more" : "";
                    return $"Conflicts with {first.With}: {first.Path.Replace('\\', '/')}{more}";
                }

                return ConflictCount == 1 ? "1 path overlaps another active mod source" : ConflictCount + " paths overlap other active mod sources";
            }
        }

        private bool _showConflicts;

        /// <summary>Whether the full list of conflicting files is open under the row.</summary>
        public bool ShowConflicts
        {
            get => _showConflicts;
            set
            {
                _showConflicts = value;
                OnPropertyChanged(nameof(ShowConflicts));
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? value.ToString("0") + " " + units[unit] : value.ToString("0.##") + " " + units[unit];
        }
    }
}
