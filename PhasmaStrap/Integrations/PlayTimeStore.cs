using PhasmaStrap.Models;
using PhasmaStrap.Models.Entities;

namespace PhasmaStrap.Integrations
{
    public static class PlayTimeStore
    {
        private const string LOG_IDENT = "PlayTimeStore";
        private const string MutexName = "PhasmaStrap-PlayTimeStore";
        private const int MaxEntries = 500;
        private const double MaxSessionMinutes = 1440.0;
        private const int DiskCheckIntervalMs = 2000;

        private static readonly object _lock = new();
        private static PlayTimeData _data = new();
        private static bool _loaded;
        private static DateTime _fileStampUtc;
        private static long _nextDiskCheckMs;

        private static readonly JsonSerializerOptions StoreJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string FolderPath => Paths.PlayTime;
        private static string FilePath => Path.Combine(FolderPath, "Data.json");

        public static void EnsureLoaded()
        {
            if (_loaded && Environment.TickCount64 < Interlocked.Read(ref _nextDiskCheckMs))
                return;

            lock (_lock)
            {
                long now = Environment.TickCount64;
                if (_loaded && now < _nextDiskCheckMs)
                    return;

                Interlocked.Exchange(ref _nextDiskCheckMs, now + DiskCheckIntervalMs);

                if (_loaded && ReadFileStamp() == _fileStampUtc)
                    return;

                Load(out _);
                _loaded = true;
            }
        }

        private static DateTime ReadFileStamp()
        {
            try
            {
                return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;
            }
            catch
            {
                return _fileStampUtc;
            }
        }

        private static bool Load(out bool unreadable)
        {
            unreadable = false;

            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(FolderPath);
                    _data = new PlayTimeData();
                    _fileStampUtc = default;
                    return true;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(FilePath);
                PlayTimeData? loaded = JsonSerializer.Deserialize<PlayTimeData>(File.ReadAllText(FilePath), StoreJsonOptions);
                _data = NormalizeData(loaded ?? new PlayTimeData());
                _fileStampUtc = stamp;
                App.Logger.WriteLine(LOG_IDENT, $"Loaded {_data.Places.Count} play time entries");
                return true;
            }
            catch (JsonException ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"{FilePath} could not be read: {ex.Message}");
                unreadable = true;
                if (!_loaded)
                    _data = new PlayTimeData();
                return false;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Load failed: {ex.Message}");
                if (!_loaded)
                    _data = new PlayTimeData();
                return false;
            }
        }

        private static PlayTimeData NormalizeData(PlayTimeData data)
        {
            data.Places = data.Places
                .Where(item => item.Key > 0 && item.Value is not null)
                .OrderByDescending(item => item.Value.LastPlayed)
                .Take(MaxEntries)
                .ToDictionary(item => item.Key, item => item.Value);

            return data;
        }

        public static IReadOnlyList<PlayTimeEntry> GetAll()
        {
            EnsureLoaded();

            lock (_lock)
            {
                return _data.Places.Values
                    .OrderByDescending(entry => entry.LastPlayed)
                    .ToList();
            }
        }

        public static void RecordSession(ActivityData? activity)
        {
            if (activity is null || activity.PlaceId <= 0)
                return;

            DateTime end = activity.TimeLeft ?? DateTime.Now;

            if (activity.TimeJoined == default || end <= activity.TimeJoined)
                return;

            double minutes = Math.Min((end - activity.TimeJoined).TotalMinutes, MaxSessionMinutes);

            if (minutes <= 0)
                return;

            bool saved = ChangeAndSave(data =>
            {
                if (!data.Places.TryGetValue(activity.PlaceId, out PlayTimeEntry? entry))
                {
                    entry = new PlayTimeEntry { PlaceId = activity.PlaceId };
                    data.Places[activity.PlaceId] = entry;
                }

                if (activity.UniverseId > 0)
                    entry.UniverseId = activity.UniverseId;

                entry.TotalMinutes += minutes;

                if (end > entry.LastPlayed)
                    entry.LastPlayed = end;

                string? name = activity.UniverseDetails?.Data?.Name;
                if (!string.IsNullOrEmpty(name))
                    entry.Name = name;

                string? icon = activity.UniverseDetails?.Thumbnail?.ImageUrl;
                if (!string.IsNullOrEmpty(icon))
                    entry.IconUrl = icon;

                return true;
            });

            App.Logger.WriteLine(LOG_IDENT, saved
                ? $"Recorded {minutes:0.#} minutes in place {activity.PlaceId}"
                : $"Could not record {minutes:0.#} minutes in place {activity.PlaceId}");
        }

        public static bool Remove(long placeId)
        {
            bool removed = ChangeAndSave(data => data.Places.Remove(placeId));
            App.Logger.WriteLine(LOG_IDENT, removed ? $"Removed place {placeId}" : $"Place {placeId} was not removed");
            return removed;
        }

        private static bool ChangeAndSave(Func<PlayTimeData, bool> change)
        {
            using var mutex = new Mutex(false, MutexName);
            bool owned = false;

            try
            {
                try { owned = mutex.WaitOne(3000); }
                catch (AbandonedMutexException) { owned = true; }

                if (!owned)
                {
                    App.Logger.WriteLine(LOG_IDENT, "The play time file is busy, skipping this change");
                    return false;
                }

                string contents;

                lock (_lock)
                {
                    if (!Load(out bool unreadable))
                    {
                        if (!unreadable)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Not saving because the play time file could not be opened, so nothing in it is lost");
                            return false;
                        }

                        string aside = FilePath + $".unreadable-{DateTime.Now:yyyyMMdd_HHmmss}";
                        File.Move(FilePath, aside, true);
                        App.Logger.WriteLine(LOG_IDENT, $"Kept the unreadable play time file as {aside}");
                        _data = new PlayTimeData();
                    }

                    _loaded = true;

                    if (!change(_data))
                        return false;

                    _data = NormalizeData(_data);
                    contents = JsonSerializer.Serialize(_data, StoreJsonOptions);
                }

                Directory.CreateDirectory(FolderPath);
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, contents);
                File.Move(tempPath, FilePath, overwrite: true);

                lock (_lock)
                    _fileStampUtc = File.GetLastWriteTimeUtc(FilePath);

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Save failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (owned)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }

        public static void Shutdown()
        {
            App.Logger.WriteLine(LOG_IDENT, "Every play time change is saved straight away, nothing left to write");
        }
    }
}
