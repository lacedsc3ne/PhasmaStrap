using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Length of a saved clip, read locally and cached. MP4 clips are read from the 'mvhd' box in the file header
    /// (a few small reads, no decoding); if that fails Media Foundation is asked instead (ClipProcessor.Probe).
    /// </summary>
    public static class ClipDurations
    {
        private const string LOG_IDENT = "ClipDurations";

        private readonly record struct Entry(long Bytes, DateTime Written, TimeSpan? Duration);

        private static readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);

        // Media Foundation probes are slow and heavy, so only one runs at a time.
        private static readonly SemaphoreSlim _probeGate = new(1, 1);

        /// <summary>Returns the cached length right away when it is known; otherwise reads it in the background and calls <paramref name="done"/> (on a worker thread).</summary>
        public static TimeSpan? Request(string path, Action<TimeSpan?> done)
        {
            if (TryCached(path, out TimeSpan? known))
                return known;

            Task.Run(() =>
            {
                TimeSpan? duration = Get(path);
                try { done(duration); }
                catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Callback failed: {ex.Message}"); }
            });

            return null;
        }

        private static bool TryCached(string path, out TimeSpan? duration)
        {
            duration = null;

            try
            {
                if (!_cache.TryGetValue(path, out Entry entry))
                    return false;

                var info = new FileInfo(path);
                if (!info.Exists || info.Length != entry.Bytes || info.LastWriteTimeUtc != entry.Written)
                    return false;

                duration = entry.Duration;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Reads the length now (blocking). Null when the file is not a clip or can't be read.</summary>
        public static TimeSpan? Get(string path)
        {
            if (TryCached(path, out TimeSpan? cached))
                return cached;

            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                    return null;
            }
            catch
            {
                return null;
            }

            TimeSpan? duration = null;

            if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    duration = ReadMp4Header(path);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Header of {Path.GetFileName(path)} unreadable: {ex.Message}");
                }

                duration ??= ProbeWithMediaFoundation(path);
            }

            _cache[path] = new Entry(info.Length, info.LastWriteTimeUtc, duration);
            return duration;
        }

        /// <summary>"0:30", "1:05", "1:02:03".</summary>
        public static string Format(TimeSpan duration)
        {
            long seconds = (long)Math.Round(Math.Max(0, duration.TotalSeconds));
            if (seconds == 0 && duration > TimeSpan.Zero)
                seconds = 1;

            return seconds >= 3600
                ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
                : $"{seconds / 60}:{seconds % 60:00}";
        }

        private static TimeSpan? ProbeWithMediaFoundation(string path)
        {
            _probeGate.Wait();
            try
            {
                TimeSpan duration = ClipProcessor.Probe(path).Duration;
                return duration > TimeSpan.Zero ? duration : null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not probe {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
            finally
            {
                _probeGate.Release();
            }
        }

        // ISO BMFF: moov > mvhd holds the movie timescale and duration.
        private static TimeSpan? ReadMp4Header(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);

            if (!FindBox(stream, 0, stream.Length, "moov", out long moovStart, out long moovEnd))
                return null;

            if (!FindBox(stream, moovStart, moovEnd, "mvhd", out long mvhdStart, out long mvhdEnd))
                return null;

            Span<byte> buffer = stackalloc byte[32];
            stream.Position = mvhdStart;

            int version = stream.ReadByte();
            if (version < 0)
                return null;

            ulong timescale, duration;

            if (version == 1)
            {
                // flags(3) creation(8) modification(8) timescale(4) duration(8)
                if (mvhdEnd - mvhdStart < 32 || stream.Read(buffer[..31]) != 31)
                    return null;

                timescale = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(19, 4));
                duration = BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(23, 8));
            }
            else
            {
                // flags(3) creation(4) modification(4) timescale(4) duration(4)
                if (mvhdEnd - mvhdStart < 20 || stream.Read(buffer[..19]) != 19)
                    return null;

                timescale = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(11, 4));
                uint shortDuration = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(15, 4));
                duration = shortDuration == uint.MaxValue ? 0 : shortDuration;
            }

            if (timescale == 0 || duration == 0 || duration == ulong.MaxValue)
                return null;

            double seconds = duration / (double)timescale;
            return seconds is > 0 and < 86400 ? TimeSpan.FromSeconds(seconds) : null;
        }

        private static bool FindBox(Stream stream, long start, long end, string type, out long payloadStart, out long boxEnd)
        {
            payloadStart = boxEnd = 0;
            Span<byte> header = stackalloc byte[16];
            long position = start;

            for (int guard = 0; position + 8 <= end && guard < 4096; guard++)
            {
                stream.Position = position;
                if (stream.Read(header[..8]) != 8)
                    return false;

                long size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
                int headerSize = 8;

                if (size == 1)
                {
                    if (stream.Read(header.Slice(8, 8)) != 8)
                        return false;
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
                    headerSize = 16;
                }
                else if (size == 0)
                {
                    size = end - position;
                }

                if (size < headerSize || position + size > end)
                    return false;

                if (header[4] == type[0] && header[5] == type[1] && header[6] == type[2] && header[7] == type[3])
                {
                    payloadStart = position + headerSize;
                    boxEnd = position + size;
                    return true;
                }

                position += size;
            }

            return false;
        }
    }
}
