using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ToolTikTokV11.Services;

public sealed record ImageMatchResult(bool Found, int X = 0, int Y = 0, double Score = 0);

public static class ImageMatcher
{
    internal sealed record Pixels(int W, int H, byte[] Bgra, int Stride);

    public sealed class PreparedBitmap : IDisposable
    {
        public Bitmap Bitmap { get; }
        internal Pixels PixelData { get; }
        public int Width => Bitmap.Width;
        public int Height => Bitmap.Height;

        public PreparedBitmap(Bitmap source)
        {
            Bitmap = ToFormat32bppArgb(source);
            PixelData = Read(Bitmap);
        }

        public void Dispose() => Bitmap.Dispose();
    }

    public readonly record struct TemplateScale(double Scale, PreparedBitmap Image);

    public sealed class MultiScaleTemplate : IDisposable
    {
        readonly List<TemplateScale> _scales;

        public MultiScaleTemplate(List<TemplateScale> scales) => _scales = scales;

        public int Count => _scales.Count;
        public IReadOnlyList<TemplateScale> Scales => _scales;

        public void Dispose()
        {
            foreach (var scale in _scales) scale.Image.Dispose();
        }
    }

    static Bitmap ToFormat32bppArgb(Bitmap bmp)
    {
        if (bmp.PixelFormat == PixelFormat.Format32bppArgb) return new Bitmap(bmp);
        var clone = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(clone);
        g.DrawImageUnscaled(bmp, 0, 0);
        return clone;
    }

    static Pixels Read(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = Math.Abs(data.Stride) * data.Height;
            var arr = new byte[bytes];
            Marshal.Copy(data.Scan0, arr, 0, bytes);
            return new Pixels(bmp.Width, bmp.Height, arr, Math.Abs(data.Stride));
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public static Bitmap FromBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var tmp = new Bitmap(ms);
        return new Bitmap(tmp);
    }

    public static Bitmap CropNormalized(Bitmap src, double rx1, double ry1, double rx2, double ry2)
    {
        rx1 = Math.Clamp(rx1, 0, 1); ry1 = Math.Clamp(ry1, 0, 1); rx2 = Math.Clamp(rx2, 0, 1); ry2 = Math.Clamp(ry2, 0, 1);
        var x1 = (int)Math.Round(Math.Min(rx1, rx2) * src.Width); var y1 = (int)Math.Round(Math.Min(ry1, ry2) * src.Height);
        var x2 = (int)Math.Round(Math.Max(rx1, rx2) * src.Width); var y2 = (int)Math.Round(Math.Max(ry1, ry2) * src.Height);
        x1 = Math.Clamp(x1, 0, Math.Max(0, src.Width - 1)); y1 = Math.Clamp(y1, 0, Math.Max(0, src.Height - 1));
        x2 = Math.Clamp(x2, x1 + 1, src.Width); y2 = Math.Clamp(y2, y1 + 1, src.Height);
        var dst = new Bitmap(x2 - x1, y2 - y1, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(src, new Rectangle(0, 0, dst.Width, dst.Height), new Rectangle(x1, y1, dst.Width, dst.Height), GraphicsUnit.Pixel);
        return dst;
    }

    public static MultiScaleTemplate PrepareMultiScaleTemplate(Bitmap needle)
    {
        double[] scales = [1.0, 0.9, 1.1, 0.8, 1.2, 0.7, 1.3];
        var prepared = new List<TemplateScale>(scales.Length);
        foreach (var scale in scales)
        {
            if (Math.Abs(scale - 1.0) < 0.0001)
            {
                prepared.Add(new TemplateScale(scale, new PreparedBitmap(needle)));
                continue;
            }

            int w = Math.Max(1, (int)Math.Round(needle.Width * scale));
            int h = Math.Max(1, (int)Math.Round(needle.Height * scale));
            using var resized = new Bitmap(needle, new Size(w, h));
            prepared.Add(new TemplateScale(scale, new PreparedBitmap(resized)));
        }
        return new MultiScaleTemplate(prepared);
    }

    public static ImageMatchResult Find(Bitmap haystack, Bitmap needle, int variation)
    {
        using var preparedHaystack = new PreparedBitmap(haystack);
        using var preparedNeedle = new PreparedBitmap(needle);
        return Find(preparedHaystack, preparedNeedle, variation);
    }

    public static ImageMatchResult Find(PreparedBitmap haystack, PreparedBitmap needle, int variation, long deadlineTimestamp = 0)
    {
        if (needle.Width <= 0 || needle.Height <= 0 || needle.Width > haystack.Width || needle.Height > haystack.Height) return new(false);
        var h = haystack.PixelData;
        var n = needle.PixelData;
        variation = Math.Clamp(variation, 0, 255);
        int allowedMismatch = Math.Max(0, (int)(n.W * n.H * 0.01));
        (int X, int Y)[] anchorPoints =
        [
            (0, 0),
            (n.W / 2, n.H / 2),
            (n.W - 1, n.H - 1),
            (n.W / 3, n.H / 3),
            (n.W * 2 / 3, n.H * 2 / 3)
        ];
        int primaryAnchor = SelectPrimaryAnchor(h, n, anchorPoints, variation, deadlineTimestamp);

        for (int y = 0; y <= h.H - n.H; y++)
        for (int x = 0; x <= h.W - n.W; x++)
        {
            ThrowIfDeadlineExceeded(deadlineTimestamp);
            var pivot = anchorPoints[primaryAnchor];
            if (!Near(h, n, x, y, pivot.X, pivot.Y, variation)) continue;

            bool anchors = true;
            for (int k = 0; k < anchorPoints.Length; k++)
            {
                if (k == primaryAnchor) continue;
                if (!Near(h, n, x, y, anchorPoints[k].X, anchorPoints[k].Y, variation))
                {
                    anchors = false;
                    break;
                }
            }
            if (!anchors) continue;

            int mismatches = 0;
            int checkedPx = 0;
            for (int ny = 0; ny < n.H; ny++)
            {
                ThrowIfDeadlineExceeded(deadlineTimestamp);
                for (int nx = 0; nx < n.W; nx++)
                {
                    checkedPx++;
                    if (!Near(h, n, x, y, nx, ny, variation) && ++mismatches > allowedMismatch) goto next;
                }
            }
            return new(true, x, y, 1.0 - (double)mismatches / Math.Max(1, checkedPx));
            next: ;
        }
        return new(false);
    }

    static bool Near(Pixels h, Pixels n, int hx, int hy, int nx, int ny, int tol)
    {
        int hi = (hy + ny) * h.Stride + (hx + nx) * 4;
        int ni = ny * n.Stride + nx * 4;
        return Math.Abs(h.Bgra[hi] - n.Bgra[ni]) <= tol
            && Math.Abs(h.Bgra[hi + 1] - n.Bgra[ni + 1]) <= tol
            && Math.Abs(h.Bgra[hi + 2] - n.Bgra[ni + 2]) <= tol;
    }

    static int SelectPrimaryAnchor(Pixels h, Pixels n, (int X, int Y)[] anchors, int variation, long deadlineTimestamp)
    {
        int bestIndex = 0;
        int bestCount = int.MaxValue;
        for (int i = 0; i < anchors.Length; i++)
        {
            ThrowIfDeadlineExceeded(deadlineTimestamp);
            var anchor = anchors[i];
            int matches = 0;
            for (int y = 0; y <= h.H - n.H; y++)
            {
                for (int x = 0; x <= h.W - n.W; x++)
                {
                    if (Near(h, n, x, y, anchor.X, anchor.Y, variation) && ++matches >= bestCount)
                        goto nextAnchor;
                }
            }

            nextAnchor:
            if (matches < bestCount)
            {
                bestCount = matches;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    static void ThrowIfDeadlineExceeded(long deadlineTimestamp)
    {
        if (deadlineTimestamp != 0 && Stopwatch.GetTimestamp() >= deadlineTimestamp)
            throw new TimeoutException("Image matching exceeded deadline.");
    }

    public static ImageMatchResult FindMultiScale(Bitmap haystack, Bitmap needle, int variation)
    {
        using var preparedHaystack = new PreparedBitmap(haystack);
        using var preparedTemplate = PrepareMultiScaleTemplate(needle);
        return FindMultiScale(preparedHaystack, preparedTemplate, variation);
    }

    public static ImageMatchResult FindMultiScale(PreparedBitmap haystack, MultiScaleTemplate needle, int variation, long deadlineTimestamp = 0)
    {
        foreach (var scale in needle.Scales)
        {
            if (scale.Image.Width > haystack.Width || scale.Image.Height > haystack.Height) continue;
            var result = Find(haystack, scale.Image, variation, deadlineTimestamp);
            if (result.Found) return result;
        }
        return new(false);
    }
}
