using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// CPU approximation of the RiShade pass chain (Integrations/RiShade/RiShadeShaders.cs), used for the
    /// before and after preview on the RiShade page. It runs the same maths per pixel on a small bundled
    /// sample picture: chromatic aberration, sharpen, clarity, colour grade, tonemap, ambient glow, grain,
    /// vignette, bloom and render scale. Debanding is left out because it is invisible at this size.
    /// </summary>
    public static class RiShadePreview
    {
        private const string LOG_IDENT = "RiShadePreview";

        /// <summary>The source picture as 0..1 floats per channel.</summary>
        public sealed class Picture
        {
            public Picture(int width, int height)
            {
                Width = width;
                Height = height;
                R = new float[width * height];
                G = new float[width * height];
                B = new float[width * height];
            }

            public int Width { get; }
            public int Height { get; }
            public float[] R { get; }
            public float[] G { get; }
            public float[] B { get; }
        }

        private static Picture? _sample;

        /// <summary>Loads Resources/RiShadeSample.png, or draws a stand in scene if it is missing. Call on the UI thread.</summary>
        public static Picture GetSample()
        {
            if (_sample is not null)
                return _sample;

            BitmapSource? source = null;
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/RiShadeSample.png"));
                if (info is not null)
                {
                    using Stream stream = info.Stream;
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    source = decoder.Frames[0];
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Sample picture not loaded, drawing one: {ex.Message}");
            }

            source ??= DrawFallbackScene();
            _sample = FromBitmap(source);
            return _sample;
        }

        private static Picture FromBitmap(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            byte[] px = new byte[w * h * 4];
            converted.CopyPixels(px, w * 4, 0);

            var pic = new Picture(w, h);
            for (int i = 0; i < w * h; i++)
            {
                pic.B[i] = px[i * 4] / 255f;
                pic.G[i] = px[i * 4 + 1] / 255f;
                pic.R[i] = px[i * 4 + 2] / 255f;
            }
            return pic;
        }

        /// <summary>A simple sky, sun, hills and blocks scene, only used if the bundled picture can't be read.</summary>
        private static BitmapSource DrawFallbackScene()
        {
            const int w = 448, h = 252;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(40, 90, 175), Color.FromRgb(150, 190, 230), 90), null, new Rect(0, 0, w, 150));
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(64, 140, 58), Color.FromRgb(94, 180, 78), 90), null, new Rect(0, 150, w, h - 150));
                var glow = new RadialGradientBrush(Color.FromArgb(160, 255, 236, 180), Color.FromArgb(0, 255, 236, 180));
                dc.DrawEllipse(glow, null, new Point(340, 52), 70, 70);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 250, 228)), null, new Point(340, 52), 16, 16);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(176, 62, 50)), null, new Rect(46, 70, 60, 120));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 226, 140)), null, new Rect(64, 90, 22, 24));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(92, 92, 100)), null, new Rect(190, 150, 68, h - 150));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(246, 206, 60)), null, new Rect(253, 146, 18, 18));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(40, 110, 220)), null, new Rect(250, 164, 24, 24));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(60, 170, 70)), null, new Rect(250, 188, 24, 24));
            }

            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Turns a rendered BGRA buffer into a frozen bitmap. Call on the UI thread.</summary>
        public static BitmapSource ToBitmap(byte[] bgra, int width, int height)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), bgra, width * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>The untouched picture as BGRA.</summary>
        public static byte[] Original(Picture src)
        {
            byte[] outPx = new byte[src.Width * src.Height * 4];
            for (int i = 0; i < src.Width * src.Height; i++)
                WritePixel(outPx, i, src.R[i], src.G[i], src.B[i]);
            return outPx;
        }

        /// <summary>
        /// Runs the effect chain over the picture with the given settings and returns BGRA at the picture's size.
        /// Safe to call off the UI thread.
        /// </summary>
        public static byte[] Render(Picture full, RiShadeSettings s)
        {
            if (!s.HasVisibleEffects)
                return Original(full);

            // Render scale: the game draws the effects at a lower size, then upscales.
            float scale = s.ResolveRenderScale();
            Picture src = scale < 0.999f ? Resize(full, Math.Max(16, (int)(full.Width * scale)), Math.Max(9, (int)(full.Height * scale))) : full;

            int w = src.Width, h = src.Height, n = w * h;
            var scene = new Picture(w, h);

            Picture? soft = null;
            if (s.ClarityStrength > 0f || s.AmbientStrength > 0f)
            {
                soft = Downsample2(src);
                soft = Blur9(soft, 1f);
            }

            float[] temp = s.ResolveColorTemp();
            float[] balance = s.ColorBalance ?? new[] { 1f, 1f, 1f };
            float[] lift = s.Lift ?? new[] { 0f, 0f, 0f };
            float[] gain = s.Gain ?? new[] { 1f, 1f, 1f };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float r, g, b;

                    if (s.ChromaEnabled)
                    {
                        float ox = s.ChromaStrength, oy = 0f;
                        if (s.ChromaRadial)
                        {
                            float dx = u - 0.5f, dy = v - 0.5f;
                            float r2 = dx * dx + dy * dy;
                            float k = s.ChromaStrength * (0.4f + r2 * 3.2f);
                            ox = dx * k;
                            oy = dy * k;
                        }
                        r = Bilinear(src.R, w, h, u - ox, v - oy);
                        g = src.G[i];
                        b = Bilinear(src.B, w, h, u + ox, v + oy);
                    }
                    else
                    {
                        r = src.R[i]; g = src.G[i]; b = src.B[i];
                    }

                    if (s.SharpenEnabled && s.SharpenStrength > 0f)
                        Sharpen(src, x, y, s, ref r, ref g, ref b);

                    if (s.ClarityStrength > 0f && soft is not null)
                    {
                        float hp = Lum(r, g, b) - Lum(Bilinear(soft.R, soft.Width, soft.Height, u, v), Bilinear(soft.G, soft.Width, soft.Height, u, v), Bilinear(soft.B, soft.Width, soft.Height, u, v));
                        float add = hp * s.ClarityStrength * (1f - Math.Abs(hp)) * 2f;
                        r = Math.Max(r + add, 0f); g = Math.Max(g + add, 0f); b = Math.Max(b + add, 0f);
                    }

                    if (s.GradeEnabled)
                    {
                        r *= temp[0] * balance[0]; g *= temp[1] * balance[1]; b *= temp[2] * balance[2];
                        r = r * (gain[0] - lift[0]) + lift[0];
                        g = g * (gain[1] - lift[1]) + lift[1];
                        b = b * (gain[2] - lift[2]) + lift[2];

                        if (Math.Abs(s.HueShift) > 0.01f)
                            HueRotate(ref r, ref g, ref b, s.HueShift);

                        float inv = 1f / Math.Max(s.Gamma, 0.01f);
                        r = MathF.Pow(Math.Max(r + s.Brightness, 0f), inv);
                        g = MathF.Pow(Math.Max(g + s.Brightness, 0f), inv);
                        b = MathF.Pow(Math.Max(b + s.Brightness, 0f), inv);
                    }

                    if (s.TonemapEnabled)
                    {
                        r = Tonemap(r, s); g = Tonemap(g, s); b = Tonemap(b, s);
                    }

                    if (s.AmbientStrength > 0f && soft is not null)
                    {
                        float sr = Bilinear(soft.R, soft.Width, soft.Height, u, v);
                        float sg = Bilinear(soft.G, soft.Width, soft.Height, u, v);
                        float sb = Bilinear(soft.B, soft.Width, soft.Height, u, v);
                        r += sr * sr * s.AmbientStrength * 0.8f;
                        g += sg * sg * s.AmbientStrength * 0.8f;
                        b += sb * sb * s.AmbientStrength * 0.8f;
                    }

                    if (s.GrainEnabled)
                    {
                        float size = Math.Max(s.GrainSize, 0.5f);
                        float gx = x / size + 37f, gy = y / size + 91f;
                        float lw = 0.35f + 0.65f * (1f - SmoothStep(0.15f, 0.9f, Lum(r, g, b)));
                        if (s.GrainColored)
                        {
                            r += (Ign(gx, gy) - 0.5f) * s.GrainStrength * lw;
                            g += (Ign(gx + 41.7f, gy + 41.7f) - 0.5f) * s.GrainStrength * lw;
                            b += (Ign(gx + 83.3f, gy + 83.3f) - 0.5f) * s.GrainStrength * lw;
                        }
                        else
                        {
                            float noise = (Ign(gx, gy) - 0.5f) * s.GrainStrength * lw;
                            r += noise; g += noise; b += noise;
                        }
                    }

                    if (s.VignetteEnabled)
                    {
                        float vx = (u - (0.5f + s.VignetteCenterX)) * 2f;
                        float vy = (v - (0.5f - s.VignetteCenterY)) * 2f;
                        float fall = MathF.Pow(Math.Max(vx * vx + vy * vy, 0f), s.VignetteFeather);
                        float vig = Math.Clamp(1f - s.VignetteStrength * SmoothStep(0.15f, 1.8f, fall), 0f, 1f);
                        r *= vig; g *= vig; b *= vig;
                    }

                    scene.R[i] = Math.Max(r, 0f);
                    scene.G[i] = Math.Max(g, 0f);
                    scene.B[i] = Math.Max(b, 0f);
                }
            }

            if (s.BloomEnabled && s.BloomStrength > 0f)
                AddBloom(scene, s);

            Picture result = scene.Width == full.Width && scene.Height == full.Height ? scene : Resize(scene, full.Width, full.Height);

            byte[] outPx = new byte[full.Width * full.Height * 4];
            for (int i = 0; i < full.Width * full.Height; i++)
                WritePixel(outPx, i, result.R[i], result.G[i], result.B[i]);
            return outPx;
        }

        #region Effects

        private static void Sharpen(Picture src, int x, int y, RiShadeSettings s, ref float r, ref float g, ref float b)
        {
            int w = src.Width, h = src.Height;
            int d = Math.Max(1, (int)Math.Round(s.SharpenRadius));
            int iN = Math.Clamp(y - d, 0, h - 1) * w + x;
            int iS = Math.Clamp(y + d, 0, h - 1) * w + x;
            int iW = y * w + Math.Clamp(x - d, 0, w - 1);
            int iE = y * w + Math.Clamp(x + d, 0, w - 1);
            float peak = -1f / Lerp(8f, 5f, Math.Clamp(s.SharpenStrength, 0f, 1f));
            float limit = s.SharpenClamp * 4f;
            float strength = Math.Clamp(s.SharpenStrength, 0f, 1f);

            r = SharpenChannel(r, src.R[iN], src.R[iS], src.R[iW], src.R[iE], peak, strength, limit);
            g = SharpenChannel(g, src.G[iN], src.G[iS], src.G[iW], src.G[iE], peak, strength, limit);
            b = SharpenChannel(b, src.B[iN], src.B[iS], src.B[iW], src.B[iE], peak, strength, limit);
        }

        private static float SharpenChannel(float c, float n, float s, float w, float e, float peak, float strength, float limit)
        {
            float mn = Math.Min(c, Math.Min(Math.Min(n, s), Math.Min(w, e)));
            float mx = Math.Max(c, Math.Max(Math.Max(n, s), Math.Max(w, e)));
            float amp = MathF.Sqrt(Math.Clamp(Math.Min(mn, 2f - mx) / Math.Max(mx, 1e-4f), 0f, 1f));
            float wt = amp * peak;
            float sharp = Math.Clamp(((n + s + w + e) * wt + c) / (1f + 4f * wt), 0f, 1f);
            float delta = Math.Clamp((sharp - c) * strength, -limit, limit);
            return Math.Clamp(c + delta, 0f, 1f);
        }

        private static float Tonemap(float c, RiShadeSettings s)
        {
            float lin = SrgbToLinear(c) * s.TonemapExposure;
            float wp = s.TonemapWhitepoint;
            switch (s.TonemapMode)
            {
                case 0:
                    return Math.Clamp(LinearToSrgb(lin * (1f + lin / (wp * wp)) / (1f + lin)), 0f, 1f);
                case 1:
                    return Math.Clamp(LinearToSrgb(Math.Clamp(lin * (2.51f * lin + 0.03f) / (lin * (2.43f * lin + 0.59f) + 0.14f), 0f, 1f)), 0f, 1f);
                case 2:
                    return Math.Clamp(LinearToSrgb(Uncharted2(lin) / Uncharted2(wp)), 0f, 1f);
                case 4:
                    // AgX needs the full colour matrix; a per channel log curve gets close enough for a preview.
                    float ev = Math.Clamp(MathF.Log2(Math.Max(lin, 1e-10f)), -12.47393f, 4.026069f);
                    float t = (ev + 12.47393f) / 16.5f;
                    return Math.Clamp(LinearToSrgb(MathF.Pow(Math.Max(AgxContrast(t), 0f), 2.2f)), 0f, 1f);
                default:
                    float f = Math.Max(0f, lin - 0.004f);
                    return Math.Clamp(f * (6.2f * f + 0.5f) / (f * (6.2f * f + 1.7f) + 0.06f), 0f, 1f);
            }
        }

        private static float Uncharted2(float x)
        {
            const float A = 0.15f, B = 0.50f, C = 0.10f, D = 0.20f, E = 0.02f, F = 0.30f;
            return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
        }

        private static float AgxContrast(float x)
        {
            float x2 = x * x, x4 = x2 * x2;
            return 15.5f * x4 * x2 - 40.14f * x4 * x + 31.96f * x4 - 6.868f * x2 * x + 0.4298f * x2 + 0.1191f * x - 0.00232f;
        }

        private static void HueRotate(ref float r, ref float g, ref float b, float degrees)
        {
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float d = max - min;
            float hue = 0f;
            if (d > 1e-6f)
            {
                if (max == r) hue = ((g - b) / d) % 6f;
                else if (max == g) hue = (b - r) / d + 2f;
                else hue = (r - g) / d + 4f;
                hue /= 6f;
            }
            float sat = max > 1e-6f ? d / max : 0f;
            hue = hue + degrees / 360f;
            hue -= MathF.Floor(hue);

            float hh = hue * 6f;
            float c = max * sat;
            float xx = c * (1f - Math.Abs(hh % 2f - 1f));
            float m = max - c;
            (float rr, float gg, float bb) = (int)hh switch
            {
                0 => (c, xx, 0f),
                1 => (xx, c, 0f),
                2 => (0f, c, xx),
                3 => (0f, xx, c),
                4 => (xx, 0f, c),
                _ => (c, 0f, xx),
            };
            r = rr + m; g = gg + m; b = bb + m;
        }

        private static void AddBloom(Picture scene, RiShadeSettings s)
        {
            Picture half = Downsample2(scene);
            float threshold = s.BloomThreshold;
            float knee = Math.Max(threshold * 0.5f, 1e-4f);
            for (int i = 0; i < half.R.Length; i++)
            {
                float br = Math.Max(half.R[i], Math.Max(half.G[i], half.B[i]));
                float soft = Math.Clamp(br - threshold + knee, 0f, 2f * knee);
                soft = soft * soft / (4f * knee);
                float contribution = Math.Clamp(Math.Max(soft, br - threshold) / Math.Max(br, 1e-4f), 0f, 1f);
                half.R[i] *= contribution; half.G[i] *= contribution; half.B[i] *= contribution;
            }

            float spread = Math.Max(s.BloomRadius, 0.5f);
            Picture level1 = Blur9(half, spread);
            Picture level2 = Blur9(Downsample2(level1), spread);
            int passes = Math.Clamp(s.BloomPasses, 1, 4);
            Picture? level3 = passes >= 3 && level2.Width > 8 && level2.Height > 8 ? Blur9(Downsample2(level2), spread) : null;

            int w = scene.Width, h = scene.Height;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float br = Sample(level1, u, v, 0) + Sample(level2, u, v, 0) + (level3 is null ? 0f : Sample(level3, u, v, 0));
                    float bg = Sample(level1, u, v, 1) + Sample(level2, u, v, 1) + (level3 is null ? 0f : Sample(level3, u, v, 1));
                    float bb = Sample(level1, u, v, 2) + Sample(level2, u, v, 2) + (level3 is null ? 0f : Sample(level3, u, v, 2));
                    scene.R[i] += br * s.BloomStrength;
                    scene.G[i] += bg * s.BloomStrength;
                    scene.B[i] += bb * s.BloomStrength;
                }
            }
        }

        #endregion

        #region Helpers

        private static float Sample(Picture p, float u, float v, int channel) =>
            Bilinear(channel == 0 ? p.R : channel == 1 ? p.G : p.B, p.Width, p.Height, u, v);

        private static float Bilinear(float[] data, int w, int h, float u, float v)
        {
            float fx = Math.Clamp(u * w - 0.5f, 0f, w - 1);
            float fy = Math.Clamp(v * h - 0.5f, 0f, h - 1);
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float tx = fx - x0, ty = fy - y0;
            float a = data[y0 * w + x0] + (data[y0 * w + x1] - data[y0 * w + x0]) * tx;
            float b = data[y1 * w + x0] + (data[y1 * w + x1] - data[y1 * w + x0]) * tx;
            return a + (b - a) * ty;
        }

        private static Picture Resize(Picture src, int w, int h)
        {
            var dst = new Picture(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    int i = y * w + x;
                    dst.R[i] = Bilinear(src.R, src.Width, src.Height, u, v);
                    dst.G[i] = Bilinear(src.G, src.Width, src.Height, u, v);
                    dst.B[i] = Bilinear(src.B, src.Width, src.Height, u, v);
                }
            }
            return dst;
        }

        private static Picture Downsample2(Picture src)
        {
            int w = Math.Max(1, src.Width / 2), h = Math.Max(1, src.Height / 2);
            var dst = new Picture(w, h);
            for (int y = 0; y < h; y++)
            {
                int sy0 = Math.Min(y * 2, src.Height - 1), sy1 = Math.Min(y * 2 + 1, src.Height - 1);
                for (int x = 0; x < w; x++)
                {
                    int sx0 = Math.Min(x * 2, src.Width - 1), sx1 = Math.Min(x * 2 + 1, src.Width - 1);
                    int a = sy0 * src.Width + sx0, b = sy0 * src.Width + sx1, c = sy1 * src.Width + sx0, d = sy1 * src.Width + sx1;
                    int i = y * w + x;
                    dst.R[i] = (src.R[a] + src.R[b] + src.R[c] + src.R[d]) * 0.25f;
                    dst.G[i] = (src.G[a] + src.G[b] + src.G[c] + src.G[d]) * 0.25f;
                    dst.B[i] = (src.B[a] + src.B[b] + src.B[c] + src.B[d]) * 0.25f;
                }
            }
            return dst;
        }

        private static readonly float[] Gauss9 = { 0.028f, 0.067f, 0.124f, 0.179f, 0.204f, 0.179f, 0.124f, 0.067f, 0.028f };

        /// <summary>The 9 tap separable blur from PSBlurH / PSBlurV, with the tap spacing scaled by <paramref name="spread"/>.</summary>
        private static Picture Blur9(Picture src, float spread)
        {
            int w = src.Width, h = src.Height;
            var tmp = new Picture(w, h);
            var dst = new Picture(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (int k = 0; k < 9; k++)
                    {
                        int sx = Math.Clamp(x + (int)MathF.Round((k - 4) * spread), 0, w - 1);
                        int j = y * w + sx;
                        r += src.R[j] * Gauss9[k]; g += src.G[j] * Gauss9[k]; b += src.B[j] * Gauss9[k];
                    }
                    int i = y * w + x;
                    tmp.R[i] = r; tmp.G[i] = g; tmp.B[i] = b;
                }
            }
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (int k = 0; k < 9; k++)
                    {
                        int sy = Math.Clamp(y + (int)MathF.Round((k - 4) * spread), 0, h - 1);
                        int j = sy * w + x;
                        r += tmp.R[j] * Gauss9[k]; g += tmp.G[j] * Gauss9[k]; b += tmp.B[j] * Gauss9[k];
                    }
                    int i = y * w + x;
                    dst.R[i] = r; dst.G[i] = g; dst.B[i] = b;
                }
            }
            return dst;
        }

        private static float Lum(float r, float g, float b) => r * 0.2126f + g * 0.7152f + b * 0.0722f;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float SmoothStep(float e0, float e1, float x)
        {
            float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static float Ign(float x, float y)
        {
            float d = x * 0.06711056f + y * 0.00583715f;
            float f = 52.9829189f * (d - MathF.Floor(d));
            return f - MathF.Floor(f);
        }

        private static float SrgbToLinear(float c)
        {
            c = Math.Max(c, 0f);
            return c < 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        private static float LinearToSrgb(float c)
        {
            c = Math.Max(c, 0f);
            return c < 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        }

        private static void WritePixel(byte[] dst, int i, float r, float g, float b)
        {
            dst[i * 4] = (byte)(Math.Clamp(b, 0f, 1f) * 255f + 0.5f);
            dst[i * 4 + 1] = (byte)(Math.Clamp(g, 0f, 1f) * 255f + 0.5f);
            dst[i * 4 + 2] = (byte)(Math.Clamp(r, 0f, 1f) * 255f + 0.5f);
            dst[i * 4 + 3] = 255;
        }

        #endregion
    }
}
