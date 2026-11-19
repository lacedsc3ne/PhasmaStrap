namespace PhasmaStrap.Utility
{
    public static class ScreenshotTaker
    {
        private static bool _pickingArea;

        public static void Take(Action<string?, bool> taken)
        {
            if (!App.Settings.Prop.ScreenshotPickArea)
            {
                Finish(ScreenshotCapture.Capture(), taken);
                return;
            }

            if (_pickingArea)
                return;

            System.Drawing.Bitmap? shot = ScreenshotCapture.Grab(out System.Drawing.Rectangle where);
            if (shot is null)
            {
                Finish(null, taken);
                return;
            }

            _pickingArea = true;
            try
            {
                UI.Elements.Dialogs.ScreenshotAreaWindow.Pick(shot, where, picked =>
                {
                    _pickingArea = false;
                    if (picked is null)
                        return;

                    using (picked)
                        Finish(ScreenshotCapture.Save(picked), taken);
                });
            }
            catch (Exception ex)
            {
                _pickingArea = false;
                App.Logger.WriteException("ScreenshotTaker::Take", ex);
                shot.Dispose();
            }
        }

        private static void Finish(string? path, Action<string?, bool> taken)
        {
            bool copied = path is not null && App.Settings.Prop.CaptureCopyScreenshotToClipboard && CopyCapture(path, image: true);

            if (path is not null)
                TidyCaptures();

            taken(path, copied);
        }

        public static void TidyCaptures()
        {
            int limitMb = App.Settings.Prop.CaptureStorageLimitMB;
            int maxAge = App.Settings.Prop.CaptureMaxAgeDays;
            if (limitMb <= 0 && maxAge <= 0)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    CaptureStorage.Log ??= message => App.Logger.WriteLine("CaptureStorage", message);
                    CaptureStorage.Result result = CaptureStorage.Enforce(
                        new[] { ScreenshotCapture.ScreenshotsDir, InstantReplayRecorder.ClipsDir },
                        limitMb * 1048576L, maxAge);

                    if (result.Removed > 0)
                        App.Logger.WriteLine("ScreenshotTaker::TidyCaptures", $"Moved {result.Removed} old capture(s) ({result.FreedBytes / 1048576.0:0} MB) to the Recycle Bin");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("ScreenshotTaker::TidyCaptures", $"Failed: {ex.Message}");
                }
            });
        }

        public static bool CopyCapture(string path, bool image)
        {
            ClipboardShare.Log ??= message => App.Logger.WriteLine("ClipboardShare", message);
            return image ? ClipboardShare.CopyImageFile(path) : ClipboardShare.CopyFile(path);
        }
    }
}
