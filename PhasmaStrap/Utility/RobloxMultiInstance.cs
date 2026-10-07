namespace PhasmaStrap.Utility
{
    /// <summary>
    /// "Multi instance" on the Deployment page. Roblox closes an older copy of itself when it finds it is not the
    /// only one, which it decides from the ROBLOX_singletonMutex. Taking that mutex ourselves before Roblox starts (in the
    /// bootstrapper, then in the watcher while Roblox runs) keeps every copy open, so alts can play side by side.
    ///
    /// Two things here are easy to get wrong and both were, in the macro, for eight releases.
    ///
    /// Owning it is the point, not holding a handle to it. A handle stops nothing: Roblox takes ownership and it is
    /// owning it that makes it close the others. Creating the mutex with initiallyOwned grants ownership only when
    /// this is the process that created it, which is precisely not the interesting case. Opening one that already
    /// exists has to be followed by actually waiting on it, and an abandoned one counts as taken.
    ///
    /// Ownership belongs to a thread, not to a process. Taking it on whichever thread happens to call and then
    /// returning means it is let go the moment that thread finishes, which for a thread pool thread is almost at
    /// once. So it is taken on a thread of its own that does nothing else until it is told to stop.
    /// </summary>
    public static class RobloxMultiInstance
    {
        private const string LOG_IDENT = "RobloxMultiInstance";
        public const string SingletonMutexName = "ROBLOX_singletonMutex";

        private static Thread? _owner;
        private static ManualResetEventSlim? _letGo;
        private static readonly object _lock = new();

        public static bool Enabled => App.Settings.Prop.AllowMultipleRoblox;

        /// <summary>Whether this process owns the mutex, so Roblox cannot.</summary>
        public static bool Holding { get; private set; }

        /// <summary>Takes Roblox's singleton mutex and keeps it until Release. Safe to call twice.</summary>
        public static void Hold()
        {
            if (!Enabled)
                return;

            lock (_lock)
            {
                if (_owner is not null)
                    return;

                var ready = new ManualResetEventSlim(false);
                _letGo = new ManualResetEventSlim(false);

                /* A background thread, so it never keeps the process alive.
                
                   The bootstrapper takes the mutex and never lets go: there is no Release on that path,
                   because holding it until the process ends is the whole intent. A foreground thread
                   waiting to be told to stop would therefore mean the bootstrapper could never exit.
                
                   Ending while it is held leaves the mutex abandoned, which the next process to want it is
                   handed cleanly, and which this code treats as taken for exactly that reason. */
                _owner = new Thread(() => Keep(ready)) { IsBackground = true, Name = "Roblox singleton" };
                _owner.Start();

                // Roblox checks the mutex as it comes up, so a launch that beats us to it is the one that
                // closes the others. Worth the wait; it is a handful of milliseconds.
                if (!ready.Wait(TimeSpan.FromSeconds(2)))
                    App.Logger.WriteLine(LOG_IDENT, "Gave up waiting for the singleton mutex to be taken");
            }
        }

        private static void Keep(ManualResetEventSlim ready)
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(false, SingletonMutexName);

                bool took;
                try
                {
                    took = mutex.WaitOne(TimeSpan.Zero);
                }
                catch (AbandonedMutexException)
                {
                    // Whoever had it went without letting go. It is ours now, which is the outcome wanted.
                    took = true;
                }

                Holding = took;
                App.Logger.WriteLine(LOG_IDENT, took
                    ? "Holding Roblox's singleton mutex"
                    : "Roblox's singleton mutex belongs to something else, leaving it alone");

                // Said now, not at the end. Whoever asked is waiting to launch and the answer is already
                // known; leaving it until the mutex is let go would hold up every launch by the full wait.
                ready.Set();

                if (!took)
                    return;

                _letGo?.Wait();

                mutex.ReleaseMutex();
                Holding = false;
                App.Logger.WriteLine(LOG_IDENT, "Let go of Roblox's singleton mutex");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not hold Roblox's singleton mutex: {ex.Message}");
                Holding = false;
            }
            finally
            {
                // Harmless a second time, and the one that matters when something threw before the answer
                // was known. A caller left waiting on a thread that has already given up is the worst of
                // both: no mutex and a two second pause before every launch.
                ready.Set();
                mutex?.Dispose();
            }
        }

        /// <summary>Lets go of the mutex and lets the thread holding it finish.</summary>
        public static void Release()
        {
            Thread? owner;
            lock (_lock)
            {
                if (_owner is null)
                    return;
                owner = _owner;
                _owner = null;
                _letGo?.Set();
            }

            // Short, because all it has to do is release and return. Not waiting at all would let the process
            // end mid release and leave the mutex abandoned, which works but is untidy.
            owner.Join(TimeSpan.FromSeconds(2));
            _letGo?.Dispose();
            _letGo = null;
            Holding = false;
        }
    }
}
