using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZoneQuanta.Platform;

// Image files used as program icons (Windows' own artwork, an editor extension's icon, a brand icon from the
// web) drawn like real program icons: transparent margins trimmed, a square tile given rounded corners, the
// same small margin on every side, resampled to the exact pixel size, and a one-colour mark that would vanish
// into the background redrawn in the text colour. Any thread; results are frozen.
internal static class IconImages
{
    public const string Prefix = "img:";
    private const int MaxSide = 2048;

    public static BitmapSource? Load(string file, int px, Color ink)
    {
        if (px <= 0 || Read(file) is not { } img) return null;
        var (w, h, pix) = img;
        if (!Trim(ref w, ref h, ref pix)) return null;
        bool tile = IsOpaqueTile(w, h, pix);
        if (tile) RoundCorners(w, h, pix, Math.Min(w, h) * 0.2);
        // Program icons leave about a sixteenth of their box empty on each side; brand marks usually do not.
        int side = Math.Max(w, h);
        int box = (int)Math.Ceiling(side / 0.88);
        var square = new byte[box * box * 4];
        int ox = (box - w) / 2, oy = (box - h) / 2;
        for (int y = 0; y < h; y++) Buffer.BlockCopy(pix, y * w * 4, square, ((y + oy) * box + ox) * 4, w * 4);
        var outPix = Resample(square, box, box, px, px);
        if (!tile) Tint(outPix, ink);
        var src = BitmapSource.Create(px, px, 96, 96, PixelFormats.Pbgra32, null, outPix, px * 4);
        src.Freeze();
        return src;
    }

    // Largest frame of an image file as premultiplied BGRA.
    public static (int W, int H, byte[] Pix)? Read(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Decode(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // PNG, ICO, JPEG, GIF and BMP only (no other installed codec ever sees the bytes); sizes are read from the
    // headers and checked before a single pixel is decoded, so a small file declaring a huge picture costs nothing.
    public static (int W, int H, byte[] Pix)? Decode(Stream stream)
    {
        try
        {
            if (!stream.CanSeek || !KnownFormat(stream)) return null;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (decoder.Frames.Count is 0 or > 64) return null;
            BitmapFrame? frame = null;
            foreach (var f in decoder.Frames)
                if (f.PixelWidth <= MaxSide && f.PixelHeight <= MaxSide
                    && (frame is null || (long)f.PixelWidth * f.PixelHeight > (long)frame.PixelWidth * frame.PixelHeight
                        || ((long)f.PixelWidth * f.PixelHeight == (long)frame.PixelWidth * frame.PixelHeight && f.Format.BitsPerPixel > frame.Format.BitsPerPixel)))
                    frame = f;
            if (frame is null || frame.PixelWidth < 8 || frame.PixelHeight < 8) return null;
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pix = new byte[w * h * 4];
            bgra.CopyPixels(pix, w * 4, 0);
            return (w, h, pix);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
                                       or OverflowException or System.Runtime.InteropServices.COMException or IOException or OutOfMemoryException)
        {
            return null;
        }
    }

    // Magic bytes of the accepted formats; a PNG must also declare at most MaxSide on each side.
    private static bool KnownFormat(Stream s)
    {
        long at = s.Position;
        var head = new byte[24];
        int n = 0, r;
        while (n < head.Length && (r = s.Read(head, n, head.Length - n)) > 0) n += r;
        s.Position = at;
        if (n < 8) return false;
        if (head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G')
            return n >= 24 && BigEndian(head, 16) is > 0 and <= MaxSide && BigEndian(head, 20) is > 0 and <= MaxSide;
        return (head[0] == 0 && head[1] == 0 && head[2] is 1 or 2 && head[3] == 0)  // ICO / CUR
            || (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)                // JPEG
            || (head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8') // GIF
            || (head[0] == 'B' && head[1] == 'M');                                    // BMP
    }

    private static long BigEndian(byte[] b, int at) => ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];

    // Crops to the visible part; false when nothing is visible.
    private static bool Trim(ref int w, ref int h, ref byte[] pix)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (pix[(y * w + x) * 4 + 3] > 8)
                {
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
        if (x1 < 0) return false;
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        if (cw == w && ch == h) return true;
        var crop = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++) Buffer.BlockCopy(pix, ((y + y0) * w + x0) * 4, crop, y * cw * 4, cw * 4);
        (w, h, pix) = (cw, ch, crop);
        return true;
    }

    // A filled square (an app-store style tile) rather than a mark on a transparent background.
    private static bool IsOpaqueTile(int w, int h, byte[] pix)
    {
        if (Math.Abs(w - h) > Math.Max(w, h) / 10) return false;
        int opaque = 0;
        for (int i = 3; i < pix.Length; i += 4) if (pix[i] > 240) opaque++;
        return opaque > w * h * 0.9;
    }

    private static void RoundCorners(int w, int h, byte[] pix, double r)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double cx = x + 0.5 < r ? r : x + 0.5 > w - r ? w - r : x + 0.5;
                double cy = y + 0.5 < r ? r : y + 0.5 > h - r ? h - r : y + 0.5;
                double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                double cover = Math.Clamp(r - d + 0.5, 0, 1); // anti-aliased edge
                if (cover >= 1) continue;
                int i = (y * w + x) * 4;
                for (int c = 0; c < 4; c++) pix[i + c] = (byte)Math.Round(pix[i + c] * cover);
            }
    }

    // Area average when shrinking, bilinear when enlarging; premultiplied, so edges keep their colour.
    public static byte[] Resample(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        if (sw == dw && sh == dh)
        {
            Buffer.BlockCopy(src, 0, dst, 0, dst.Length);
            return dst;
        }
        if (dw <= sw && dh <= sh)
        {
            double fx = (double)sw / dw, fy = (double)sh / dh;
            var acc = new double[4];
            for (int y = 0; y < dh; y++)
            {
                double sy0 = y * fy, sy1 = sy0 + fy;
                for (int x = 0; x < dw; x++)
                {
                    double sx0 = x * fx, sx1 = sx0 + fx, area = 0;
                    Array.Clear(acc);
                    for (int yy = (int)sy0; yy < Math.Min(sh, (int)Math.Ceiling(sy1)); yy++)
                    {
                        double wy = Math.Min(yy + 1, sy1) - Math.Max(yy, sy0);
                        for (int xx = (int)sx0; xx < Math.Min(sw, (int)Math.Ceiling(sx1)); xx++)
                        {
                            double wgt = wy * (Math.Min(xx + 1, sx1) - Math.Max(xx, sx0));
                            int i = (yy * sw + xx) * 4;
                            for (int c = 0; c < 4; c++) acc[c] += src[i + c] * wgt;
                            area += wgt;
                        }
                    }
                    int o = (y * dw + x) * 4;
                    for (int c = 0; c < 4; c++) dst[o + c] = (byte)Math.Clamp(Math.Round(acc[c] / Math.Max(area, 1e-9)), 0, 255);
                }
            }
            return dst;
        }
        for (int y = 0; y < dh; y++)
        {
            double sy = Math.Clamp((y + 0.5) * sh / dh - 0.5, 0, sh - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, sh - 1);
            double ty = sy - y0;
            for (int x = 0; x < dw; x++)
            {
                double sx = Math.Clamp((x + 0.5) * sw / dw - 0.5, 0, sw - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, sw - 1);
                double tx = sx - x0;
                int o = (y * dw + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    double top = src[(y0 * sw + x0) * 4 + c] * (1 - tx) + src[(y0 * sw + x1) * 4 + c] * tx;
                    double bottom = src[(y1 * sw + x0) * 4 + c] * (1 - tx) + src[(y1 * sw + x1) * 4 + c] * tx;
                    dst[o + c] = (byte)Math.Clamp(Math.Round(top * (1 - ty) + bottom * ty), 0, 255);
                }
            }
        }
        return dst;
    }

    // A program icon that is a one-colour mark (a Store app's unplated glyph) redrawn like an image icon.
    public static BitmapSource Contrasted(BitmapSource icon, Color ink)
    {
        var bgra = icon.Format == PixelFormats.Pbgra32 ? icon : new FormatConvertedBitmap(icon, PixelFormats.Pbgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var pix = new byte[w * h * 4];
        bgra.CopyPixels(pix, w * 4, 0);
        if (IsOpaqueTile(w, h, pix) || !Tint(pix, ink)) return icon;
        var src = BitmapSource.Create(w, h, icon.DpiX, icon.DpiY, PixelFormats.Pbgra32, null, pix, w * 4);
        src.Freeze();
        return src;
    }

    // A grey mark far from the text colour sits too close to the background (a black logo on a dark theme):
    // redraw it in the text colour, keeping its shape.
    private static bool Tint(byte[] pix, Color ink)
    {
        int seen = 0, grey = 0;
        double lum = 0, lum2 = 0;
        for (int i = 0; i < pix.Length; i += 4)
        {
            int a = pix[i + 3];
            if (a < 64) continue;
            double b = pix[i] * 255.0 / a, g = pix[i + 1] * 255.0 / a, r = pix[i + 2] * 255.0 / a;
            seen++;
            if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 30) grey++;
            double l = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
            lum += l;
            lum2 += l * l;
        }
        if (seen == 0 || grey < seen * 0.92) return false;
        double mean = lum / seen;
        // One tone only: a white glyph on a black disc is grey too, but recolouring would flatten it.
        if (lum2 / seen - mean * mean > 0.02) return false;
        double inkLum = (0.2126 * ink.R + 0.7152 * ink.G + 0.0722 * ink.B) / 255;
        if (Math.Abs(mean - inkLum) <= 0.45) return false;
        for (int i = 0; i < pix.Length; i += 4)
        {
            int a = pix[i + 3];
            pix[i] = (byte)(ink.B * a / 255);
            pix[i + 1] = (byte)(ink.G * a / 255);
            pix[i + 2] = (byte)(ink.R * a / 255);
        }
        return true;
    }
}
