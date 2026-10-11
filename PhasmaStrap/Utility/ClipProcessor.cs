using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace PhasmaStrap.Utility
{
    public sealed class ClipInfo
    {
        public int Width;
        public int Height;
        public double Fps;
        public TimeSpan Duration;
        public long FileBytes;
    }

    public sealed class ClipEditOptions
    {
        public TimeSpan Start = TimeSpan.Zero;
        public TimeSpan End = TimeSpan.MaxValue;

        public int CropX, CropY, CropWidth, CropHeight;

        public double Speed = 1.0;
    }

    public static class ClipProcessor
    {
        public static Action<string>? Log;

        private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
        private const int FirstAudioStream = unchecked((int)0xFFFFFFFD);
        private const int AllStreams = unchecked((int)0xFFFFFFFE);
        private const int MediaSource = unchecked((int)0xFFFFFFFF);
        private const int FlagEndOfStream = 0x2;

        private static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("FB394F3D-CCF1-42EE-BBB3-F9B845D5681D");
        private static readonly Guid MF_PD_DURATION = new("6C990D33-BB8E-477A-8598-0D5D96FCD88A");
        private static readonly Guid MFSampleExtension_CleanPoint = new("9cdf01d8-a0f0-43ba-b077-eaa06cbd728a");
        private static readonly Guid Nv12 = new("3231564E-0000-0010-8000-00AA00389B71");

        public static bool CanCopy(ClipEditOptions options) =>
            (options.CropWidth <= 0 || options.CropHeight <= 0) && Math.Abs(options.Speed - 1.0) < 0.001;

        private static readonly object _mfLock = new();
        private static int _mfRefs;

        private static void Startup()
        {
            lock (_mfLock)
            {
                if (_mfRefs++ == 0)
                    MediaFactory.MFStartup(false);
            }
        }

        private static void Shutdown()
        {
            lock (_mfLock)
            {
                if (--_mfRefs == 0)
                {
                    try { MediaFactory.MFShutdown(); } catch { }
                }
            }
        }

        public static ClipInfo Probe(string path)
        {
            Startup();
            try
            {
                using Reader reader = Reader.Open(path);
                return reader.Info;
            }
            finally
            {
                Shutdown();
            }
        }

        public static byte[]? GrabFrame(string path, TimeSpan position, out int width, out int height)
        {
            width = height = 0;
            Startup();
            try
            {
                using Reader reader = Reader.Open(path);
                width = reader.Info.Width;
                height = reader.Info.Height;

                if (position > TimeSpan.Zero)
                    reader.Seek(position.Ticks);

                byte[]? last = null;
                byte[] scratch = new byte[width * height * 4];

                while (reader.Read(out IMFSample? sample, out long ts))
                {
                    if (sample is null)
                        continue;

                    using (sample)
                    {
                        if (ts > position.Ticks && last is not null)
                            break;

                        reader.CopyFrame(sample, 0, 0, width, height, scratch);
                        last ??= new byte[scratch.Length];
                        Buffer.BlockCopy(scratch, 0, last, 0, scratch.Length);

                        if (ts >= position.Ticks)
                            break;
                    }
                }

                return last;
            }
            finally
            {
                Shutdown();
            }
        }

        public sealed class Thumbnail
        {
            public byte[] Bgra = Array.Empty<byte>();
            public int Width;
            public int Height;
        }

        public static List<Thumbnail> GrabThumbnails(string path, int count, int maxWidth, CancellationToken cancel = default)
        {
            var result = new List<Thumbnail>();
            Startup();
            try
            {
                using Reader reader = Reader.Open(path);
                ClipInfo info = reader.Info;
                if (info.Duration <= TimeSpan.Zero || count <= 0)
                    return result;

                int step = Math.Max(1, (int)Math.Ceiling((double)info.Width / maxWidth));
                int tw = info.Width / step, th = info.Height / step;
                byte[] scratch = new byte[info.Width * info.Height * 4];
                long interval = info.Duration.Ticks / count;
                int sought = -1;

                while (result.Count < count)
                {
                    cancel.ThrowIfCancellationRequested();

                    if (sought != result.Count)
                    {
                        sought = result.Count;
                        if (sought > 0)
                            reader.Seek(sought * interval);
                    }

                    if (!reader.Read(out IMFSample? sample, out long _))
                        break;

                    if (sample is null)
                        continue;

                    using (sample)
                    {
                        reader.CopyFrame(sample, 0, 0, info.Width, info.Height, scratch);

                        var thumb = new Thumbnail { Width = tw, Height = th, Bgra = new byte[tw * th * 4] };
                        for (int y = 0; y < th; y++)
                        {
                            int srcRow = y * step * info.Width * 4;
                            int dstRow = y * tw * 4;
                            for (int x = 0; x < tw; x++)
                                Buffer.BlockCopy(scratch, srcRow + x * step * 4, thumb.Bgra, dstRow + x * 4, 4);
                        }

                        result.Add(thumb);
                    }
                }

                return result;
            }
            finally
            {
                Shutdown();
            }
        }

        public static void Export(string source, string destination, ClipEditOptions options, Action<double>? progress = null, CancellationToken cancel = default)
        {
            if (CanCopy(options))
            {
                try
                {
                    CopyTrim(source, destination, options, progress, cancel);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Could not cut the clip without re-encoding ({ex.Message.Trim()}), re-encoding it instead");
                }
            }

            Startup();

            IMFSinkWriter? writer = null;
            bool finished = false;

            try
            {
                using Reader reader = Reader.Open(source, nv12: true);
                ClipInfo info = reader.Info;

                double speed = Math.Clamp(options.Speed, 0.25, 4.0);
                long startTicks = Math.Max(0, options.Start.Ticks);
                long endTicks = options.End == TimeSpan.MaxValue ? long.MaxValue : options.End.Ticks;
                if (endTicks <= startTicks)
                    throw new ArgumentException("The trim end has to be after the trim start.");

                int cropX = 0, cropY = 0, outW = info.Width, outH = info.Height;
                if (options.CropWidth > 0 && options.CropHeight > 0)
                {
                    cropX = Math.Clamp(options.CropX, 0, info.Width - 2) & ~1;
                    cropY = Math.Clamp(options.CropY, 0, info.Height - 2) & ~1;
                    outW = Math.Clamp(options.CropWidth, 2, info.Width - cropX);
                    outH = Math.Clamp(options.CropHeight, 2, info.Height - cropY);
                }

                outW &= ~1;
                outH &= ~1;
                if (outW < 64 || outH < 64)
                    throw new ArgumentException("The crop area is too small - it has to be at least 64 x 64 pixels.");

                double outFps = Math.Clamp(Math.Min(info.Fps * speed, info.Fps), 1, 240);
                long frameGap = (long)(10_000_000 / outFps);
                long sourceSpan = Math.Min(endTicks, info.Duration.Ticks) - startTicks;

                double sourceBitrate = info.Duration.TotalSeconds > 0.1 ? info.FileBytes * 8 / info.Duration.TotalSeconds : 4_000_000;
                double area = (double)(outW * outH) / (info.Width * info.Height);
                uint bitrate = (uint)Math.Clamp(sourceBitrate * 1.3 * area, 1_000_000, 20_000_000);

                Log?.Invoke($"Export {source} -> {destination}: {startTicks / 1e7:0.00}s-{(endTicks == long.MaxValue ? info.Duration.TotalSeconds : endTicks / 1e7):0.00}s crop={cropX},{cropY} {outW}x{outH} speed={speed} fps={outFps:0.##} bitrate={bitrate}");

                if (startTicks > 0)
                    reader.Seek(startTicks);

                using AudioPass? audio = Math.Abs(speed - 1.0) < 0.001 ? AudioPass.TryOpen(source, startTicks) : null;

                var attempts = new List<(bool Hardware, double Fps)> { (true, outFps), (false, outFps) };
                if (outFps > 60)
                {
                    attempts.Add((true, 60));
                    attempts.Add((false, 60));
                }

                int streamIndex = 0;
                Exception? refused = null;

                foreach ((bool hardware, double declaredFps) in attempts)
                {
                    try
                    {
                        writer = CreateSinkWriter(destination, outW, outH, declaredFps, bitrate, hardware, out streamIndex);
                        audio?.AddTo(writer);
                        writer.BeginWriting();

                        if (refused is not null)
                            Log?.Invoke($"Encoding with the {(hardware ? "hardware" : "software")} encoder, stream declared as {declaredFps:0.##}fps");

                        refused = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        refused = ex;
                        Log?.Invoke($"{(hardware ? "Hardware" : "Software")} encoder refused {outW}x{outH}@{declaredFps:0.##}: {ex.Message.Trim()}");
                        writer?.Dispose();
                        writer = null;
                        try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                    }
                }

                if (writer is null)
                    throw refused ?? new InvalidOperationException("No H.264 encoder is available.");

                int frameBytes = outW * outH * 3 / 2;
                int written = 0;
                long nextFrame = 0;

                while (reader.Read(out IMFSample? sample, out long ts))
                {
                    cancel.ThrowIfCancellationRequested();

                    if (sample is null)
                        continue;

                    using (sample)
                    {
                        if (ts < startTicks)
                            continue;
                        if (ts >= endTicks)
                            break;

                        long outTime = (long)((ts - startTicks) / speed);
                        if (outTime + frameGap / 2 < nextFrame)
                            continue;

                        nextFrame = Math.Max(nextFrame, outTime) + frameGap;

                        using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(frameBytes);
                        buffer.Lock(out IntPtr ptr, out int _, out int _);
                        try
                        {
                            reader.CopyNv12(sample, cropX, cropY, outW, outH, ptr);
                        }
                        finally
                        {
                            buffer.Unlock();
                        }
                        buffer.CurrentLength = frameBytes;

                        using IMFSample outSample = MediaFactory.MFCreateSample();
                        outSample.AddBuffer(buffer);
                        outSample.SampleTime = outTime;
                        outSample.SampleDuration = frameGap;

                        writer.WriteSample(streamIndex, outSample);
                        written++;

                        audio?.Pump(writer, ts, startTicks, endTicks);

                        if (sourceSpan > 0)
                            progress?.Invoke(Math.Clamp((double)(ts - startTicks) / sourceSpan, 0, 1));
                    }
                }

                if (written == 0)
                    throw new InvalidOperationException("There are no frames between the trim start and end.");

                audio?.Pump(writer, long.MaxValue, startTicks, endTicks);

                Log?.Invoke($"Export read {written} frame(s), closing the file");
                writer.Finalize();
                finished = true;
                progress?.Invoke(1);
                Log?.Invoke($"Export wrote {written} frame(s){(audio is null ? "" : $" and {audio.Written} block(s) of sound")}");
            }
            finally
            {
                writer?.Dispose();
                Shutdown();

                if (!finished)
                {
                    try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                }
            }
        }

        public static void ExportGif(string source, string destination, ClipEditOptions options, GifExportOptions gif, Action<double>? progress = null, CancellationToken cancel = default)
        {
            Startup();
            bool finished = false;
            GCHandle pinned = default;

            try
            {
                using Reader reader = Reader.Open(source, nv12: true);
                ClipInfo info = reader.Info;

                double speed = Math.Clamp(options.Speed, 0.25, 4.0);
                long startTicks = Math.Max(0, options.Start.Ticks);
                long endTicks = options.End == TimeSpan.MaxValue ? long.MaxValue : options.End.Ticks;
                if (endTicks <= startTicks)
                    throw new ArgumentException("The trim end has to be after the trim start.");

                int cropX = 0, cropY = 0, cropW = info.Width, cropH = info.Height;
                if (options.CropWidth > 0 && options.CropHeight > 0)
                {
                    cropX = Math.Clamp(options.CropX, 0, info.Width - 2) & ~1;
                    cropY = Math.Clamp(options.CropY, 0, info.Height - 2) & ~1;
                    cropW = Math.Clamp(options.CropWidth, 2, info.Width - cropX);
                    cropH = Math.Clamp(options.CropHeight, 2, info.Height - cropY);
                }

                cropW &= ~1;
                cropH &= ~1;

                int outW = cropW, outH = cropH;
                if (gif.MaxWidth > 0 && cropW > gif.MaxWidth)
                {
                    outW = gif.MaxWidth;
                    outH = Math.Max(1, (int)Math.Round(cropH * (double)outW / cropW));
                }

                int fps = Math.Clamp(gif.Fps, 1, 50);
                long interval = 10_000_000 / fps;
                long sourceSpan = Math.Min(endTicks, info.Duration.Ticks) - startTicks;

                Log?.Invoke($"ExportGif {source} -> {destination}: {startTicks / 1e7:0.00}s-{(endTicks == long.MaxValue ? info.Duration.TotalSeconds : endTicks / 1e7):0.00}s crop={cropX},{cropY} {cropW}x{cropH} -> {outW}x{outH} speed={speed} fps={fps} dither={gif.Dither}");

                if (startTicks > 0)
                    reader.Seek(startTicks);

                byte[] full = new byte[cropW * cropH * 4];
                byte[] planes = new byte[cropW * cropH * 3 / 2];
                pinned = GCHandle.Alloc(planes, GCHandleType.Pinned);
                byte[] scaled = outW == cropW && outH == cropH ? full : new byte[outW * outH * 4];
                byte[] pending = new byte[outW * outH * 4];
                long pendingTime = -1;
                long nextPick = 0;
                long writtenCs = 0;

                using (var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var writer = new GifWriter(file, outW, outH, gif.Dither))
                {
                    void Flush(long untilTime)
                    {
                        long targetCs = (long)Math.Round(untilTime / 100_000.0);
                        int delay = (int)Math.Max(2, targetCs - writtenCs);
                        writer.AddFrame(pending, delay);
                        writtenCs += delay;
                    }

                    while (reader.Read(out IMFSample? sample, out long ts))
                    {
                        cancel.ThrowIfCancellationRequested();

                        if (sample is null)
                            continue;

                        using (sample)
                        {
                            if (ts < startTicks)
                                continue;
                            if (ts >= endTicks)
                                break;

                            long outTime = (long)((ts - startTicks) / speed);
                            if (outTime < nextPick)
                                continue;

                            while (nextPick <= outTime)
                                nextPick += interval;

                            if (pendingTime >= 0)
                                Flush(outTime);

                            reader.CopyNv12(sample, cropX, cropY, cropW, cropH, pinned.AddrOfPinnedObject());
                            Nv12ToBgra(planes, cropW, cropH, full);
                            if (!ReferenceEquals(scaled, full))
                                Downscale(full, cropW, cropH, scaled, outW, outH);

                            Buffer.BlockCopy(scaled, 0, pending, 0, pending.Length);
                            pendingTime = outTime;

                            if (sourceSpan > 0)
                                progress?.Invoke(Math.Clamp((double)(ts - startTicks) / sourceSpan, 0, 1));
                        }
                    }

                    if (pendingTime < 0)
                        throw new InvalidOperationException("There are no frames between the trim start and end.");

                    Flush(pendingTime + interval);

                    Log?.Invoke($"ExportGif wrote {writer.FrameCount} frame(s), {writtenCs / 100.0:0.00}s");
                }

                finished = true;
                progress?.Invoke(1);
            }
            finally
            {
                if (pinned.IsAllocated)
                    pinned.Free();

                Shutdown();

                if (!finished)
                {
                    try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                }
            }
        }

        private static unsafe void Nv12ToBgra(byte[] nv12, int width, int height, byte[] bgra)
        {
            fixed (byte* luma = nv12)
            fixed (byte* target = bgra)
            {
                byte* chroma = luma + width * height;

                for (int y = 0; y < height; y++)
                {
                    byte* row = luma + y * width;
                    byte* pairs = chroma + (y / 2) * width;
                    byte* output = target + y * width * 4;

                    for (int x = 0; x < width; x++)
                    {
                        int c = 298 * (row[x] - 16) + 128;
                        int u = pairs[x & ~1] - 128;
                        int v = pairs[(x & ~1) + 1] - 128;

                        output[x * 4] = Clamp((c + 541 * u) >> 8);
                        output[x * 4 + 1] = Clamp((c - 55 * u - 136 * v) >> 8);
                        output[x * 4 + 2] = Clamp((c + 459 * v) >> 8);
                        output[x * 4 + 3] = 255;
                    }
                }
            }

            static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
        }

        private static void Downscale(byte[] source, int sw, int sh, byte[] destination, int dw, int dh)
        {
            int[] x0 = new int[dw + 1];
            for (int x = 0; x <= dw; x++)
                x0[x] = (int)((long)x * sw / dw);

            for (int y = 0; y < dh; y++)
            {
                int top = (int)((long)y * sh / dh);
                int bottom = Math.Max(top + 1, (int)((long)(y + 1) * sh / dh));

                for (int x = 0; x < dw; x++)
                {
                    int left = x0[x];
                    int right = Math.Max(left + 1, x0[x + 1]);

                    int b = 0, g = 0, r = 0;
                    for (int sy = top; sy < bottom; sy++)
                    {
                        int i = (sy * sw + left) * 4;
                        for (int sx = left; sx < right; sx++, i += 4)
                        {
                            b += source[i];
                            g += source[i + 1];
                            r += source[i + 2];
                        }
                    }

                    int n = (bottom - top) * (right - left);
                    int o = (y * dw + x) * 4;
                    destination[o] = (byte)(b / n);
                    destination[o + 1] = (byte)(g / n);
                    destination[o + 2] = (byte)(r / n);
                    destination[o + 3] = 255;
                }
            }
        }

        private static void CopyTrim(string source, string destination, ClipEditOptions options, Action<double>? progress, CancellationToken cancel)
        {
            Startup();

            IMFSinkWriter? writer = null;
            IMFMediaType? audioType = null;
            IMFSample? pendingAudio = null;
            bool finished = false;

            try
            {
                int hr = MFCreateSourceReaderFromURL(source, IntPtr.Zero, out IntPtr ptr);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                using var reader = new IMFSourceReader(ptr);

                TimeSpan length = TimeSpan.Zero;
                try
                {
                    object? value = reader.GetPresentationAttribute(MediaSource, MF_PD_DURATION).Value;
                    if (value is not null)
                        length = TimeSpan.FromTicks(Convert.ToInt64(value));
                }
                catch
                {
                }

                long startTicks = Math.Max(0, options.Start.Ticks);
                long endTicks = options.End == TimeSpan.MaxValue ? long.MaxValue : options.End.Ticks;
                if (endTicks <= startTicks)
                    throw new ArgumentException("The trim end has to be after the trim start.");

                long span = Math.Min(endTicks, length.Ticks > 0 ? length.Ticks : endTicks) - startTicks;

                reader.SetStreamSelection(AllStreams, false);
                reader.SetStreamSelection(FirstVideoStream, true);

                using IMFMediaType videoType = reader.GetNativeMediaType(FirstVideoStream, 0);
                SetCurrentMediaType(reader, FirstVideoStream, videoType);

                try
                {
                    reader.SetStreamSelection(FirstAudioStream, true);
                    audioType = reader.GetNativeMediaType(FirstAudioStream, 0);
                    SetCurrentMediaType(reader, FirstAudioStream, audioType);
                }
                catch
                {
                    audioType?.Dispose();
                    audioType = null;
                    try { reader.SetStreamSelection(FirstAudioStream, false); } catch { }
                }

                using (IMFAttributes attributes = MediaFactory.MFCreateAttributes(1))
                {
                    SetUInt32(attributes, MfInterop.MF_SINK_WRITER_DISABLE_THROTTLING, 1);

                    hr = MFCreateSinkWriterFromURL(destination, IntPtr.Zero, attributes.NativePointer, out IntPtr sink);
                    if (hr < 0)
                        Marshal.ThrowExceptionForHR(hr);

                    writer = new IMFSinkWriter(sink);
                }

                int videoOut = writer.AddStream(videoType);
                writer.SetInputMediaType(videoOut, videoType, null);

                int audioOut = -1;
                if (audioType is not null)
                {
                    audioOut = writer.AddStream(audioType);
                    writer.SetInputMediaType(audioOut, audioType, null);
                }

                writer.BeginWriting();

                if (startTicks > 0)
                {
                    SeekReader(reader, startTicks);

                    long keyframe = -1;
                    while (keyframe < 0)
                    {
                        reader.ReadSample(FirstVideoStream, 0, out int _, out int flags, out long ts, out IMFSample? first);

                        using (first)
                        {
                            if ((flags & FlagEndOfStream) != 0)
                                break;

                            if (first is not null && TryGetUInt32(first, MFSampleExtension_CleanPoint, out uint clean) && clean != 0)
                                keyframe = ts;
                        }
                    }

                    if (keyframe >= 0)
                        SeekReader(reader, keyframe);
                }

                long baseTicks = -1;
                long pendingTime = 0;
                bool audioDone = audioOut < 0;
                int frames = 0, blocks = 0;

                void PumpAudio(long untilTicks)
                {
                    while (!audioDone)
                    {
                        if (pendingAudio is null)
                        {
                            reader.ReadSample(FirstAudioStream, 0, out int _, out int flags, out pendingTime, out pendingAudio);

                            if ((flags & FlagEndOfStream) != 0)
                            {
                                pendingAudio?.Dispose();
                                pendingAudio = null;
                                audioDone = true;
                                return;
                            }

                            if (pendingAudio is null)
                                continue;
                        }

                        if (pendingTime >= endTicks)
                        {
                            audioDone = true;
                            return;
                        }

                        if (pendingTime > untilTicks)
                            return;

                        if (pendingTime >= baseTicks)
                        {
                            pendingAudio.SampleTime = pendingTime - baseTicks;
                            writer.WriteSample(audioOut, pendingAudio);
                            blocks++;
                        }

                        pendingAudio.Dispose();
                        pendingAudio = null;
                    }
                }

                while (true)
                {
                    cancel.ThrowIfCancellationRequested();

                    reader.ReadSample(FirstVideoStream, 0, out int _, out int flags, out long ts, out IMFSample? sample);

                    if ((flags & FlagEndOfStream) != 0)
                    {
                        sample?.Dispose();
                        break;
                    }

                    if (sample is null)
                        continue;

                    using (sample)
                    {
                        if (ts >= endTicks)
                            break;

                        if (baseTicks < 0)
                        {
                            if (!TryGetUInt32(sample, MFSampleExtension_CleanPoint, out uint clean) || clean == 0)
                                continue;

                            baseTicks = ts;
                        }

                        sample.SampleTime = ts - baseTicks;
                        writer.WriteSample(videoOut, sample);
                        frames++;

                        if (span > 0)
                            progress?.Invoke(Math.Clamp((double)(ts - startTicks) / span, 0, 1));
                    }

                    PumpAudio(ts);
                }

                if (frames == 0)
                    throw new InvalidOperationException("There are no frames between the trim start and end.");

                PumpAudio(long.MaxValue);

                writer.Finalize();
                finished = true;
                progress?.Invoke(1);

                Log?.Invoke($"Copied {frames} frame(s){(audioOut < 0 ? "" : $" and {blocks} block(s) of sound")} without re-encoding, starting at the keyframe at {baseTicks / 1e7:0.00}s for a cut at {startTicks / 1e7:0.00}s");
            }
            finally
            {
                pendingAudio?.Dispose();
                audioType?.Dispose();
                writer?.Dispose();
                Shutdown();

                if (!finished)
                {
                    try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                }
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetCurrentPositionFn(IntPtr self, ref Guid format, IntPtr position);

        private static void SeekReader(IMFSourceReader reader, long ticks)
        {
            IntPtr position = Marshal.AllocHGlobal(24);

            try
            {
                for (int offset = 0; offset < 24; offset += 8)
                    Marshal.WriteInt64(position, offset, 0);

                Marshal.WriteInt16(position, 0, 20);
                Marshal.WriteInt64(position, 8, ticks);

                Guid format = Guid.Empty;
                IntPtr vtable = Marshal.ReadIntPtr(reader.NativePointer);
                var fn = Marshal.GetDelegateForFunctionPointer<SetCurrentPositionFn>(Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size));

                int hr = fn(reader.NativePointer, ref format, position);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
            finally
            {
                Marshal.FreeHGlobal(position);
            }
        }

        private sealed class AudioPass : IDisposable
        {
            private readonly IMFSourceReader _reader;
            private readonly IMFMediaType _pcm;
            private readonly uint _rate, _channels;
            private int _stream = -1;
            private IMFSample? _pending;
            private long _pendingTime;
            private bool _done;

            public int Written { get; private set; }

            private AudioPass(IMFSourceReader reader, IMFMediaType pcm, uint rate, uint channels)
            {
                _reader = reader;
                _pcm = pcm;
                _rate = rate;
                _channels = channels;
            }

            public static AudioPass? TryOpen(string path, long startTicks)
            {
                IMFSourceReader? reader = null;
                IMFMediaType? current = null;

                try
                {
                    int hr = MFCreateSourceReaderFromURL(path, IntPtr.Zero, out IntPtr ptr);
                    if (hr < 0)
                        Marshal.ThrowExceptionForHR(hr);

                    reader = new IMFSourceReader(ptr);
                    reader.SetStreamSelection(AllStreams, false);
                    reader.SetStreamSelection(FirstAudioStream, true);

                    using (IMFMediaType wanted = MediaFactory.MFCreateMediaType())
                    {
                        wanted.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                        wanted.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                        SetCurrentMediaType(reader, FirstAudioStream, wanted);
                    }

                    current = reader.GetCurrentMediaType(FirstAudioStream);

                    if (!TryGetUInt32(current, MediaTypeAttributeKeys.AudioSamplesPerSecond, out uint rate)
                        || !TryGetUInt32(current, MediaTypeAttributeKeys.AudioNumChannels, out uint channels)
                        || (rate != 44100 && rate != 48000) || channels is < 1 or > 2)
                    {
                        Log?.Invoke("The clip's sound is not something the AAC encoder takes - exporting without it");
                        current.Dispose();
                        reader.Dispose();
                        return null;
                    }

                    if (startTicks > 0)
                        SeekReader(reader, startTicks);

                    return new AudioPass(reader, current, rate, channels);
                }
                catch (Exception)
                {
                    current?.Dispose();
                    reader?.Dispose();
                    return null;
                }
            }

            public void AddTo(IMFSinkWriter writer)
            {
                using IMFMediaType output = MediaFactory.MFCreateMediaType();
                output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                SetUInt32(output, MediaTypeAttributeKeys.AudioSamplesPerSecond, _rate);
                SetUInt32(output, MediaTypeAttributeKeys.AudioNumChannels, _channels);
                SetUInt32(output, MediaTypeAttributeKeys.AudioBitsPerSample, 16);
                SetUInt32(output, MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000);

                _stream = writer.AddStream(output);
                writer.SetInputMediaType(_stream, _pcm, null);
            }

            public void Pump(IMFSinkWriter writer, long untilTicks, long startTicks, long endTicks)
            {
                while (!_done && _stream >= 0)
                {
                    if (_pending is null)
                    {
                        _reader.ReadSample(FirstAudioStream, 0, out int _, out int flags, out _pendingTime, out _pending);

                        if ((flags & FlagEndOfStream) != 0)
                        {
                            _pending?.Dispose();
                            _pending = null;
                            Finish(writer);
                            return;
                        }

                        if (_pending is null)
                            continue;
                    }

                    if (_pendingTime >= endTicks)
                    {
                        Finish(writer);
                        return;
                    }

                    if (_pendingTime > untilTicks)
                        return;

                    long duration = 0;
                    try { duration = _pending.SampleDuration; } catch { }

                    if (_pendingTime + duration > startTicks)
                    {
                        _pending.SampleTime = Math.Max(0, _pendingTime - startTicks);
                        writer.WriteSample(_stream, _pending);
                        Written++;
                    }

                    _pending.Dispose();
                    _pending = null;
                }
            }

            private void Finish(IMFSinkWriter writer)
            {
                _done = true;

                try
                {
                    writer.NotifyEndOfSegment(_stream);
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Could not close the sound stream early: {ex.Message.Trim()}");
                }
            }

            public void Dispose()
            {
                _pending?.Dispose();
                _pcm.Dispose();
                _reader.Dispose();
            }
        }

        private sealed class Reader : IDisposable
        {
            private readonly IMFSourceReader _reader;
            private readonly int _stride;
            private readonly int _planeHeight;

            public ClipInfo Info { get; }

            private Reader(IMFSourceReader reader, ClipInfo info, int stride, int planeHeight)
            {
                _reader = reader;
                Info = info;
                _stride = stride;
                _planeHeight = planeHeight;
            }

            public static Reader Open(string path, bool nv12 = false)
            {
                IntPtr ptr;
                using (IMFAttributes attributes = MediaFactory.MFCreateAttributes(1))
                {
                    if (!nv12)
                        SetUInt32(attributes, MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);

                    int hr = MFCreateSourceReaderFromURL(path, attributes.NativePointer, out ptr);
                    if (hr < 0)
                        Marshal.ThrowExceptionForHR(hr);
                }

                var reader = new IMFSourceReader(ptr);

                try
                {
                    double fps = 12;
                    int shownWidth = 0, shownHeight = 0;
                    using (IMFMediaType native = reader.GetNativeMediaType(FirstVideoStream, 0))
                    {
                        if (TryGetUInt64(native, MediaTypeAttributeKeys.FrameRate, out ulong rate) && (uint)rate != 0)
                            fps = (double)(uint)(rate >> 32) / (uint)rate;

                        if (TryGetUInt64(native, MediaTypeAttributeKeys.FrameSize, out ulong nativeSize))
                        {
                            shownWidth = (int)(uint)(nativeSize >> 32);
                            shownHeight = (int)(uint)nativeSize;
                        }
                    }

                    using (IMFMediaType wanted = MediaFactory.MFCreateMediaType())
                    {
                        wanted.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                        wanted.Set(MediaTypeAttributeKeys.Subtype, nv12 ? Nv12 : VideoFormatGuids.Rgb32);
                        SetCurrentMediaType(reader, FirstVideoStream, wanted);
                    }

                    int width, height, stride, planeHeight;
                    using (IMFMediaType current = reader.GetCurrentMediaType(FirstVideoStream))
                    {
                        if (!TryGetUInt64(current, MediaTypeAttributeKeys.FrameSize, out ulong size))
                            throw new InvalidOperationException("The clip has no readable frame size.");

                        width = (int)(uint)(size >> 32);
                        height = (int)(uint)size;
                        planeHeight = height;

                        stride = TryGetUInt32(current, MediaTypeAttributeKeys.DefaultStride, out uint s) ? unchecked((int)s) : nv12 ? width : -(width * 4);
                    }

                    if (nv12 && shownWidth > 0 && shownHeight > 0 && shownWidth <= width && shownHeight <= height)
                    {
                        width = shownWidth;
                        height = shownHeight;
                    }

                    TimeSpan duration = TimeSpan.Zero;
                    try
                    {
                        object? value = reader.GetPresentationAttribute(MediaSource, MF_PD_DURATION).Value;
                        if (value is not null)
                            duration = TimeSpan.FromTicks(Convert.ToInt64(value));
                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"Duration attribute unavailable: {ex.Message}");
                    }

                    var info = new ClipInfo
                    {
                        Width = width,
                        Height = height,
                        Fps = fps,
                        Duration = duration,
                        FileBytes = new FileInfo(path).Length,
                    };

                    return new Reader(reader, info, stride, planeHeight);
                }
                catch
                {
                    reader.Dispose();
                    throw;
                }
            }

            public void Seek(long ticks) => SeekReader(_reader, ticks);

            public void CopyNv12(IMFSample sample, int x, int y, int w, int h, IntPtr destination)
            {
                using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();

                IMF2DBuffer? buffer2D = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
                if (buffer2D is not null)
                {
                    using (buffer2D)
                    {
                        buffer2D.Lock2D(out IntPtr scanline0, out int pitch);
                        try
                        {
                            CopyPlanes(scanline0, pitch, x, y, w, h, destination);
                        }
                        finally
                        {
                            buffer2D.Unlock2D();
                        }
                    }

                    return;
                }

                buffer.Lock(out IntPtr ptr, out int _, out int _);
                try
                {
                    CopyPlanes(ptr, Math.Abs(_stride), x, y, w, h, destination);
                }
                finally
                {
                    buffer.Unlock();
                }
            }

            private unsafe void CopyPlanes(IntPtr luma, int pitch, int x, int y, int w, int h, IntPtr destination)
            {
                byte* source = (byte*)luma;
                byte* target = (byte*)destination;

                for (int row = 0; row < h; row++)
                    Buffer.MemoryCopy(source + (long)(y + row) * pitch + x, target + (long)row * w, w, w);

                byte* chroma = source + (long)_planeHeight * pitch;
                byte* chromaTarget = target + (long)w * h;

                for (int row = 0; row < h / 2; row++)
                    Buffer.MemoryCopy(chroma + (long)(y / 2 + row) * pitch + x, chromaTarget + (long)row * w, w, w);
            }

            public bool Read(out IMFSample? sample, out long timestamp)
            {
                _reader.ReadSample(FirstVideoStream, 0, out int _, out int flags, out timestamp, out sample);

                if ((flags & FlagEndOfStream) != 0)
                {
                    sample?.Dispose();
                    sample = null;
                    return false;
                }

                return true;
            }

            public void CopyFrame(IMFSample sample, int x, int y, int w, int h, byte[] destination)
            {
                using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();

                IMF2DBuffer? buffer2D = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
                if (buffer2D is not null)
                {
                    using (buffer2D)
                    {
                        buffer2D.Lock2D(out IntPtr scanline0, out int pitch);
                        try
                        {
                            CopyRows(scanline0, pitch, x, y, w, h, destination);
                        }
                        finally
                        {
                            buffer2D.Unlock2D();
                        }
                    }

                    return;
                }

                buffer.Lock(out IntPtr ptr, out int _, out int _);
                try
                {
                    int abs = Math.Abs(_stride);
                    IntPtr top = _stride < 0 ? ptr + (Info.Height - 1) * abs : ptr;
                    CopyRows(top, _stride, x, y, w, h, destination);
                }
                finally
                {
                    buffer.Unlock();
                }
            }

            private static void CopyRows(IntPtr topRow, int pitch, int x, int y, int w, int h, byte[] destination)
            {
                int rowBytes = w * 4;
                for (int row = 0; row < h; row++)
                {
                    IntPtr src = topRow + (y + row) * pitch + x * 4;
                    Marshal.Copy(src, destination, row * rowBytes, rowBytes);
                }
            }

            public void Dispose() => _reader.Dispose();
        }

        private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");

        private static IMFSinkWriter CreateSinkWriter(string path, int width, int height, double fps, uint bitrate, bool hardware, out int streamIndex)
        {
            uint fpsNum = (uint)Math.Round(fps * 1000);
            const uint fpsDen = 1000;

            using IMFMediaType outputType = MediaFactory.MFCreateMediaType();
            outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            outputType.Set(MediaTypeAttributeKeys.AvgBitrate, bitrate);
            outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            SetUInt64(outputType, MediaTypeAttributeKeys.FrameSize, Pack((uint)width, (uint)height));
            SetUInt64(outputType, MediaTypeAttributeKeys.FrameRate, Pack(fpsNum, fpsDen));
            SetUInt64(outputType, MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));

            using IMFMediaType inputType = MediaFactory.MFCreateMediaType();
            inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inputType.Set(MediaTypeAttributeKeys.Subtype, Nv12);
            inputType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            SetUInt64(inputType, MediaTypeAttributeKeys.FrameSize, Pack((uint)width, (uint)height));
            SetUInt64(inputType, MediaTypeAttributeKeys.FrameRate, Pack(fpsNum, fpsDen));
            SetUInt64(inputType, MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));

            inputType.Set(MediaTypeAttributeKeys.DefaultStride, (uint)width);

            using IMFAttributes attributes = MediaFactory.MFCreateAttributes(2);
            SetUInt32(attributes, MfInterop.MF_SINK_WRITER_DISABLE_THROTTLING, 1);
            if (hardware)
                SetUInt32(attributes, MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);

            int hr = MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes.NativePointer, out IntPtr ptr);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            var writer = new IMFSinkWriter(ptr);

            try
            {
                streamIndex = writer.AddStream(outputType);
                writer.SetInputMediaType(streamIndex, inputType, null);
                return writer;
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }

        private static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MFCreateSinkWriterFromURL(string pwszOutputURL, IntPtr pByteStream, IntPtr pAttributes, out IntPtr ppSinkWriter);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MFCreateSourceReaderFromURL(string pwszURL, IntPtr pAttributes, out IntPtr ppSourceReader);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetCurrentMediaTypeFn(IntPtr self, int streamIndex, IntPtr reserved, IntPtr mediaType);

        private static void SetCurrentMediaType(IMFSourceReader reader, int streamIndex, IMFMediaType mediaType)
        {
            IntPtr vtable = Marshal.ReadIntPtr(reader.NativePointer);
            var fn = Marshal.GetDelegateForFunctionPointer<SetCurrentMediaTypeFn>(Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size));
            int hr = fn(reader.NativePointer, streamIndex, IntPtr.Zero, mediaType.NativePointer);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetUInt32Fn(IntPtr self, ref Guid key, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetUInt64Fn(IntPtr self, ref Guid key, out ulong value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUInt32Fn(IntPtr self, ref Guid key, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUInt64Fn(IntPtr self, ref Guid key, ulong value);

        private static T Slot<T>(IMFAttributes attributes, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(attributes.NativePointer);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        }

        private static bool TryGetUInt32(IMFAttributes attributes, Guid key, out uint value)
            => Slot<GetUInt32Fn>(attributes, 7)(attributes.NativePointer, ref key, out value) >= 0;

        private static bool TryGetUInt64(IMFAttributes attributes, Guid key, out ulong value)
            => Slot<GetUInt64Fn>(attributes, 8)(attributes.NativePointer, ref key, out value) >= 0;

        private static void SetUInt32(IMFAttributes attributes, Guid key, uint value)
        {
            int hr = Slot<SetUInt32Fn>(attributes, 21)(attributes.NativePointer, ref key, value);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        private static void SetUInt64(IMFAttributes attributes, Guid key, ulong value)
        {
            int hr = Slot<SetUInt64Fn>(attributes, 22)(attributes.NativePointer, ref key, value);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }
    }
}
