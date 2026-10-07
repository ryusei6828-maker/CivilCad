using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>画像ファイルの大きさと位置 (ワールドファイル / GeoTIFF があれば) 。</summary>
public sealed record ImageInfo(int PixelWidth, int PixelHeight, (Vec2 Origin, Vec2 U, Vec2 V)? Placement, string? PlacementSource);

/// <summary>
/// 画像の読み込み。オルソ画像のような大きな画像でも画面が止まらないよう、
/// ・読み込みはすべて裏 (別スレッド) で行い、終わったら Updated で知らせる
/// ・全体は縮小版 (長辺 PreviewMax 画素) だけをメモリに持つ
/// ・拡大して縮小版では粗くなったら、見えている範囲だけを必要な細かさで読み直す (Detail)
/// </summary>
public static class ImageCache
{
    /// <summary>全体表示用の縮小版の長辺 (画素)。</summary>
    public const int PreviewMax = 2048;

    /// <summary>部分読み直しの出力の上限 (画素)。</summary>
    public const int DetailMax = 4096;

    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".pdf"];

    public sealed class Entry
    {
        public string Path { get; init; } = "";

        /// <summary>PDF のページ (0 から)。画像では 0。</summary>
        public int Page { get; init; }
        public bool Loading { get; internal set; } = true;
        public string? Error { get; internal set; }
        public BitmapSource? Preview { get; internal set; }

        /// <summary>見えている範囲を細かく読み直したもの。Region は元画像の画素の範囲。</summary>
        public (Int32Rect Region, BitmapSource Bitmap, double Scale)? Detail { get; internal set; }

        internal (Int32Rect Region, int OutW, int OutH)? PendingDetail;
        internal bool DetailRunning;
    }

    private static readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>画像の読み込み (縮小版・部分) が終わった。別スレッドから呼ばれる。</summary>
    public static event EventHandler<string>? Updated;

    /// <summary>画像を取り出す。初めてなら裏で読み込みを始め、読み込み中の Entry を返す (待たない)。page は PDF のページ。</summary>
    public static Entry Get(string path, int page = 0)
    {
        return _entries.GetOrAdd($"{path}|{page}", _ =>
        {
            var entry = new Entry { Path = path, Page = page };
            Task.Run(() =>
            {
                try
                {
                    entry.Preview = LoadPreview(path, page);
                }
                catch (Exception ex)
                {
                    entry.Error = File.Exists(path) ? $"{(PdfRenderer.IsPdf(path) ? "PDF" : "画像")}を読み込めません ({ex.Message})" : "ファイルが見つかりません";
                }
                entry.Loading = false;
                Updated?.Invoke(null, path);
            });
            return entry;
        });
    }

    /// <summary>読み込み済みの画像を捨てる (ファイルが差し替えられたとき用)。PDF は全ページ。</summary>
    public static void Forget(string path)
    {
        foreach (var key in _entries.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            _entries.TryRemove(key, out _);
    }

    /// <summary>
    /// 元画像の Region の範囲を OutW × OutH 画素で読み直すよう頼む (待たない)。
    /// 読み直し中に次の依頼が来たら、終わってから最後の依頼だけを行う。
    /// </summary>
    public static void RequestDetail(Entry entry, Int32Rect region, int outW, int outH)
    {
        lock (entry)
        {
            entry.PendingDetail = (region, outW, outH);
            if (entry.DetailRunning) return;
            entry.DetailRunning = true;
        }
        Task.Run(() =>
        {
            while (true)
            {
                (Int32Rect Region, int OutW, int OutH) req;
                lock (entry)
                {
                    if (entry.PendingDetail is not { } r)
                    {
                        entry.DetailRunning = false;
                        return;
                    }
                    req = r;
                    entry.PendingDetail = null;
                }
                try
                {
                    var bmp = PdfRenderer.IsPdf(entry.Path)
                        ? PdfRenderer.Render(entry.Path, entry.Page, req.Region, req.OutW, req.OutH)
                        : LoadRegion(entry.Path, req.Region, req.OutW, req.OutH);
                    entry.Detail = (req.Region, bmp, (double)req.OutW / req.Region.Width);
                    Updated?.Invoke(null, entry.Path);
                }
                catch
                {
                    // 部分読み直しに失敗しても縮小版は出ているので、そのままにする
                }
            }
        });
    }

    /// <summary>画像の大きさと位置情報を読む (画素は読まないので大きな画像でもすぐ終わる)。</summary>
    public static ImageInfo ReadInfo(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        int w = frame.PixelWidth, h = frame.PixelHeight;

        foreach (var wf in WorldFile.Candidates(path))
        {
            if (!File.Exists(wf)) continue;
            if (WorldFile.Parse(File.ReadAllText(wf)) is WorldFile world)
                return new ImageInfo(w, h, ImageEntity.FromWorldFile(world, w, h), Path.GetFileName(wf));
        }
        if (ReadGeoTiff(frame) is WorldFile geo)
            return new ImageInfo(w, h, ImageEntity.FromWorldFile(geo, w, h), "GeoTIFF");
        return new ImageInfo(w, h, null, null);
    }

    /// <summary>GeoTIFF の ModelPixelScale (33550) と ModelTiepoint (33922) からワールドファイル相当の値を作る。</summary>
    private static WorldFile? ReadGeoTiff(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is not BitmapMetadata md) return null;
            if (md.GetQuery("/ifd/{ushort=33550}") is not double[] scale || scale.Length < 2) return null;
            if (md.GetQuery("/ifd/{ushort=33922}") is not double[] tie || tie.Length < 6) return null;
            // tie: (i, j, k) の画素の角が (x, y, z)。画素の大きさは scale (y は下向きが正)
            var sx = scale[0];
            var sy = scale[1];
            var cornerX = tie[3] - tie[0] * sx;
            var cornerY = tie[4] + tie[1] * sy;
            return new WorldFile(sx, 0, 0, -sy, cornerX + sx / 2, cornerY - sy / 2);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource LoadPreview(string path, int page)
    {
        if (PdfRenderer.IsPdf(path))
        {
            var info = PdfRenderer.ReadPages(path)[page];
            var (pw, ph) = PdfRenderer.PixelSize(info);
            var k = Math.Min(1.0, (double)PreviewMax / Math.Max(pw, ph));
            return PdfRenderer.Render(path, page, null, (int)Math.Round(pw * k), (int)Math.Round(ph * k));
        }
        int w, h;
        using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var f = BitmapDecoder.Create(s, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            w = f.PixelWidth;
            h = f.PixelHeight;
        }
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
        // JPEG は縮小しながら読めるので、大きな画像でも速く・少ないメモリで済む
        if (Math.Max(w, h) > PreviewMax)
        {
            if (w >= h) bmp.DecodePixelWidth = PreviewMax;
            else bmp.DecodePixelHeight = PreviewMax;
        }
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource LoadRegion(string path, Int32Rect region, int outW, int outH)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var frame = BitmapDecoder.Create(s, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
        BitmapSource src = new CroppedBitmap(frame, region);
        if (outW != region.Width || outH != region.Height)
            src = new TransformedBitmap(src, new ScaleTransform((double)outW / region.Width, (double)outH / region.Height));
        // ここで画素を読み切る (ファイルを閉じたあと画面側で使えるように)
        var result = new WriteableBitmap(src);
        result.Freeze();
        return result;
    }
}
