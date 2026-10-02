using System.Net.Http;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.Utility
{
    public static class ScreenshotShare
    {
        private const string LOG_IDENT = "ScreenshotShare";
        private const int MaxSide = 1920;
        private const long MaxBytes = 2 * 1024 * 1024;

        public static byte[]? Prepare(string path) => Prepare(path, false, null);

        /// <summary>
        /// Shrinks the screenshot to a JPEG for the gallery. <paramref name="hideNames"/> pixelates the areas set up for
        /// Stream Safe (chat and player list by default), where usernames show; <paramref name="stamp"/> adds a small label in the corner.
        /// </summary>
        public static byte[]? Prepare(string path, bool hideNames, string? stamp)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);

                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];

                double scale = Math.Min(1.0, Math.Min((double)MaxSide / frame.PixelWidth, (double)MaxSide / frame.PixelHeight));

                BitmapSource sized = scale < 1.0
                    ? new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale))
                    : frame;

                if (hideNames || !string.IsNullOrWhiteSpace(stamp))
                    sized = Decorate(sized, hideNames, stamp);

                foreach (int quality in new[] { 88, 75, 60, 45 })
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                    encoder.Frames.Add(BitmapFrame.Create(sized));

                    using var output = new MemoryStream();
                    encoder.Save(output);

                    if (output.Length <= MaxBytes)
                        return output.ToArray();
                }

                App.Logger.WriteLine(LOG_IDENT, "That screenshot would not fit under the size limit");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not prepare {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        private static BitmapSource Decorate(BitmapSource source, bool hideNames, string? stamp)
        {
            int width = source.PixelWidth, height = source.PixelHeight;
            var visual = new System.Windows.Media.DrawingVisual();

            using (System.Windows.Media.DrawingContext context = visual.RenderOpen())
            {
                context.DrawImage(source, new System.Windows.Rect(0, 0, width, height));

                if (hideNames)
                {
                    foreach (PhasmaStrap.Integrations.Overlays.StreamSafeRegion region in PhasmaStrap.Integrations.Overlays.StreamSafe.Regions)
                    {
                        var area = new System.Windows.Int32Rect((int)(region.X * width), (int)(region.Y * height), (int)(region.W * width), (int)(region.H * height));
                        area.Width = Math.Min(area.Width, width - area.X);
                        area.Height = Math.Min(area.Height, height - area.Y);
                        if (area.Width < 2 || area.Height < 2)
                            continue;

                        // Shrink the area to a few blocks, then draw it back up without smoothing: a pixelated patch.
                        double block = Math.Max(8, Math.Min(width, height) / 60.0);
                        var cropped = new CroppedBitmap(source, area);
                        var tiny = new TransformedBitmap(cropped, new System.Windows.Media.ScaleTransform(
                            Math.Max(1, area.Width / block) / area.Width, Math.Max(1, area.Height / block) / area.Height));

                        var patch = new System.Windows.Media.ImageDrawing(tiny, new System.Windows.Rect(area.X, area.Y, area.Width, area.Height));
                        var group = new System.Windows.Media.DrawingGroup();
                        System.Windows.Media.RenderOptions.SetBitmapScalingMode(group, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                        group.Children.Add(patch);
                        context.DrawDrawing(group);
                    }
                }

                if (!string.IsNullOrWhiteSpace(stamp))
                {
                    double size = Math.Max(12, height / 48.0);
                    var text = new System.Windows.Media.FormattedText(stamp.Trim(), CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                        new System.Windows.Media.Typeface("Segoe UI Semibold"), size, System.Windows.Media.Brushes.White, 1.0);

                    double pad = size * 0.5;
                    var box = new System.Windows.Rect(pad, height - text.Height - pad * 3, text.Width + pad * 2, text.Height + pad * 2);
                    var shade = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 0, 0, 0));
                    shade.Freeze();

                    context.DrawRoundedRectangle(shade, null, box, pad, pad);
                    context.DrawText(text, new System.Windows.Point(box.X + pad, box.Y + pad));
                }
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }

        public static async Task<string?> ShareAsync(string path, string name, string summary) => await ShareAsync(path, name, summary, false, null);

        public static async Task<string?> ShareAsync(string path, string name, string summary, bool hideNames, string? stamp)
        {
            if (!PhasmaAccount.SignedIn)
                return "Sign in to your PhasmaStrap account first.";

            byte[]? bytes = Prepare(path, hideNames, stamp);

            if (bytes is null)
                return "That screenshot could not be read, or it is too large even after shrinking.";

            try
            {
                string query = $"?name={Uri.EscapeDataString(name ?? "")}&summary={Uri.EscapeDataString(summary ?? "")}";

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{App.ServerBase}/v1/shots/new{query}");
                PhasmaAccount.Authorize(request);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                using HttpResponseMessage response = await App.HttpClient.SendAsync(request);
                string text = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Shared {Path.GetFileName(path)} ({bytes.Length} bytes)");
                    return null;
                }

                App.Logger.WriteLine(LOG_IDENT, $"The gallery refused it ({(int)response.StatusCode}): {text}");

                try
                {
                    using JsonDocument document = JsonDocument.Parse(text);
                    return document.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() : "The gallery would not take it.";
                }
                catch
                {
                    return "The gallery would not take it.";
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Sharing failed: {ex.Message}");
                return ex.Message;
            }
        }
    }
}
