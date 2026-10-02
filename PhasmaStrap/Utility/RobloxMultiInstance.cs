namespace PhasmaStrap.Utility
{
    /// <summary>
    /// "Allow more than one Roblox" on the Deployment page. Roblox closes an older copy of itself when it finds it is not the
    /// only one, which it decides from the ROBLOX_singletonMutex. Holding that mutex ourselves before Roblox starts (in the
    /// bootstrapper, then in the watcher while Roblox runs) keeps every copy open, so alts can play side by side.
    /// </summary>
    public static class RobloxMultiInstance
    {
        private const string LOG_IDENT = "RobloxMultiInstance";
        public const string SingletonMutexName = "ROBLOX_singletonMutex";

        private static Mutex? _held;
        private static readonly object _lock = new();

        public static bool Enabled => App.Settings.Prop.AllowMultipleRoblox;

        /// <summary>Opens (or creates) Roblox's singleton mutex and keeps it for the life of this process. Safe to call twice.</summary>
        public static void Hold()
        {
            if (!Enabled)
                return;

            lock (_lock)
            {
                if (_held is not null)
                    return;

                try
                {
                    _held = new Mutex(true, SingletonMutexName, out bool createdNew);
                    App.Logger.WriteLine(LOG_IDENT, createdNew ? "Holding Roblox's singleton mutex" : "Roblox's singleton mutex already exists, keeping a handle to it");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not hold Roblox's singleton mutex: {ex.Message}");
                    _held = null;
                }
            }
        }

        /// <summary>Lets go of the handle. Closing it is enough; ownership is dropped when the process ends.</summary>
        public static void Release()
        {
            lock (_lock)
            {
                try { _held?.Dispose(); } catch { }
                _held = null;
            }
        }
    }
}
