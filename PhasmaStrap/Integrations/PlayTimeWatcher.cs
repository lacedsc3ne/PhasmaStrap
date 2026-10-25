using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Integrations
{
    public sealed class PlayTimeWatcher : IDisposable
    {
        private const string LOG_IDENT = "PlayTimeWatcher";

        private readonly ActivityWatcher _activityWatcher;

        private readonly List<Task> _pending = new();

        private bool _disposed;

        public PlayTimeWatcher(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _activityWatcher.OnGameLeave += OnGameLeave;
        }

        private void OnGameLeave(object? sender, EventArgs e)
        {
            ActivityData? activity;

            lock (_activityWatcher.History)
                activity = _activityWatcher.History.FirstOrDefault();

            if (activity is null || activity.PlaceId <= 0 || !activity.TimeLeft.HasValue)
                return;

            Task record = RecordAsync(activity);
            lock (_pending)
            {
                _pending.RemoveAll(task => task.IsCompleted);
                _pending.Add(record);
            }
        }

        private async Task RecordAsync(ActivityData activity)
        {
            try
            {
                if (activity.UniverseDetails is null && activity.UniverseId > 0)
                {
                    try
                    {
                        await UniverseDetails.FetchSingle(activity.UniverseId).ConfigureAwait(false);
                        activity.UniverseDetails = UniverseDetails.LoadFromCache(activity.UniverseId);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to fetch universe details for {activity.UniverseId}: {ex.Message}");
                    }
                }

                PlayTimeStore.RecordSession(activity);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _activityWatcher.OnGameLeave -= OnGameLeave;

            Task[] pending;
            lock (_pending)
                pending = _pending.Where(task => !task.IsCompleted).ToArray();

            if (pending.Length > 0)
            {
                bool finished = Task.WaitAll(pending, TimeSpan.FromSeconds(5));
                App.Logger.WriteLine(LOG_IDENT, finished
                    ? $"Waited for {pending.Length} play time record(s) to save"
                    : $"{pending.Length} play time record(s) were still saving after 5 seconds");
            }

            GC.SuppressFinalize(this);
        }
    }
}
