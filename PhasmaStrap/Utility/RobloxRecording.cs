namespace PhasmaStrap.Utility
{
    public static class RobloxRecording
    {
        private const string LOG_IDENT = "RobloxRecording";

        private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Roblox");

        private static string Kept => Folder + " (before blocking)";

        public static bool Blocked => File.Exists(Folder) && !Directory.Exists(Folder);

        public static bool Set(bool block)
        {
            try
            {
                if (block == Blocked)
                    return true;

                if (block)
                {
                    if (Directory.Exists(Folder))
                    {
                        if (Directory.EnumerateFileSystemEntries(Folder).Any())
                        {
                            if (Directory.Exists(Kept))
                                throw new IOException($"\"{Path.GetFileName(Kept)}\" already exists in your Videos folder. Move it somewhere first.");

                            Directory.Move(Folder, Kept);
                        }
                        else
                        {
                            Directory.Delete(Folder);
                        }
                    }

                    File.WriteAllText(Folder, "PhasmaStrap put this file here so Roblox cannot save recordings. Turn the setting off to remove it.");
                    File.SetAttributes(Folder, FileAttributes.ReadOnly);

                    App.Logger.WriteLine(LOG_IDENT, "Roblox can no longer save recordings");
                    return true;
                }

                File.SetAttributes(Folder, FileAttributes.Normal);
                File.Delete(Folder);

                if (Directory.Exists(Kept))
                    Directory.Move(Kept, Folder);

                App.Logger.WriteLine(LOG_IDENT, "Roblox can save recordings again");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not {(block ? "block" : "restore")} Roblox recordings: {ex.Message}");
                Frontend.ShowMessageBox($"Could not change that:\n{ex.Message}", System.Windows.MessageBoxImage.Warning);
                return false;
            }
        }
    }
}
