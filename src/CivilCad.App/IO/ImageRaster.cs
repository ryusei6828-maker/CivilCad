using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CivilCad.Core.Model;
using SkiaSharp;

namespace CivilCad.App.IO;

/// <summary>印刷用に画像 (JPG / PNG / TIFF など、PDF のページ) を SkiaSharp の画像として読む。</summary>
public static class ImageRaster
{
    /// <summary>長辺の画素数の上限 (大きな画像でもメモリを使いすぎないように)。</summary>
    public const int MaxPixels = 8000;

    /// <summary>
    /// 横 wantWidth 画素くらいで読む (元の画素数より細かくはしない)。読めなければ null。
    /// </summary>
    public static SKBitmap? Load(ImageEntity img, int wantWidth)
    {
        try
        {
            var k = Math.Min(1.0, Math.Min((double)Math.Max(wantWidth, 16) / img.PixelWidth, (double)MaxPixels / Math.Max(img.PixelWidth, img.PixelHeight)));
            int w = Math.Max(1, (int)Math.Round(img.PixelWidth * k)), h = Math.Max(1, (int)Math.Round(img.PixelHeight * k));
            BitmapSource src;
            if (PdfRenderer.IsPdf(img.Path))
            {
                src = PdfRenderer.Render(img.Path, img.Page, null, w, h);
            }
            else
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(Path.GetFullPath(img.Path));
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                if (w < img.PixelWidth) bmp.DecodePixelWidth = w;
                bmp.EndInit();
                src = bmp;
            }
            return ToSkia(src);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static SKBitmap ToSkia(BitmapSource src)
    {
        var bgra = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var result = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bgra.CopyPixels(new System.Windows.Int32Rect(0, 0, w, h), result.GetPixels(), result.ByteCount, result.RowBytes);
        return result;
    }
}
