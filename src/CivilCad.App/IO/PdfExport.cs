using System.IO;
using CivilCad.App.Drawing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using SkiaSharp;

namespace CivilCad.App.IO;

/// <summary>PDF に印刷するときの設定。</summary>
public sealed class PdfOptions
{
    /// <summary>用紙 (A0〜A4)。null なら自動 (図枠に合う A 判、無ければ図形が余白付きで収まる A 判)。</summary>
    public string? Paper { get; set; }

    /// <summary>縦置き (既定は横置き)。</summary>
    public bool Portrait { get; set; }

    /// <summary>印刷の縮尺 (分母)。null なら図面の縮尺。</summary>
    public double? ScaleDenominator { get; set; }

    /// <summary>すべて黒で印刷する (塗りつぶしは灰色の濃さにする)。</summary>
    public bool Monochrome { get; set; }

    /// <summary>線幅の指定が無い図形の線幅 (mm)。</summary>
    public double DefaultLineWeightMm { get; set; } = 0.13;

    /// <summary>
    /// 文字をフォントのまま埋め込む (PDF の中で文字を検索・コピーできる)。日本語のフォントは丸ごと入るので数 MB 大きくなる。
    /// false なら文字は形 (アウトライン) で描く (見た目は同じ)。
    /// </summary>
    public bool EmbedFonts { get; set; }

    /// <summary>印刷範囲の選び方 (窓で覚えておく用。実際の範囲は <see cref="Region"/> と <see cref="Only"/>)。</summary>
    public PdfRange Range { get; set; } = PdfRange.Drawing;

    /// <summary>範囲が用紙 (余白を除く) にちょうど収まる縮尺にする。用紙を指定しなければ A3。</summary>
    public bool FitToPaper { get; set; }

    /// <summary>印刷する範囲 (図面座標 m)。範囲の外は印刷しない。null なら図面全体。</summary>
    public Box2? Region { get; set; }

    /// <summary>この図形だけ印刷する (選択した図形)。null ならすべて。</summary>
    public IReadOnlyCollection<Entity>? Only { get; set; }
}

/// <summary>印刷範囲。</summary>
public enum PdfRange
{
    /// <summary>図面全体 (図枠があれば図枠)。</summary>
    Drawing,

    /// <summary>今の画面に見えている範囲。</summary>
    Screen,

    /// <summary>選択した図形だけ。</summary>
    Selection,

    /// <summary>2 点で指定した四角の範囲。</summary>
    Window,
}

/// <summary>
/// 図面を PDF に印刷する (SkiaSharp の PDF 出力。線・文字はベクトルのまま、日本語の文字はフォントを埋め込む)。
/// 用紙と位置の決め方は SFC の保存と同じ (図枠があればそれに合う A 判、無ければ図形全体が収まる A 判の中央)。
/// 画面で白い線 (暗い背景用の既定色) は黒で印刷する。非表示のレイヤは印刷しない。
/// </summary>
public static class PdfExport
{
    private const float PtPerMm = 72f / 25.4f;
    private const double Margin = 20; // 図枠が無いときの用紙余白 (mm)

    /// <summary>点を印刷する黒丸の直径 (用紙 mm)。</summary>
    public const double PointDiameterMm = 0.5;

    /// <summary>用紙 (幅・高さ mm) と、用紙の左下に当たる図面座標 (m)、用紙 1mm が図面で何 m か。</summary>
    public sealed record Layout(string PaperName, double Width, double Height, Vec2 Origin, double MetersPerMm);

    /// <summary>「用紙に合わせる」ときの用紙の余白 (mm)。</summary>
    public const double FitMargin = 10;

    /// <summary>「用紙に合わせる」で用紙を指定しないときの用紙。</summary>
    public const string FitDefaultPaper = "A3";

    public static Layout ComputeLayout(CadDocument doc, PdfOptions o)
    {
        var target = TargetBox(doc, o);
        if (o.FitToPaper)
        {
            // 範囲 (無ければ図枠・図形全体) が、余白を除いた用紙にちょうど収まる縮尺
            var fitPaper = PaperSize.All.FirstOrDefault(x => x.Name == (o.Paper ?? FitDefaultPaper)) ?? PaperSize.All.First(x => x.Name == FitDefaultPaper);
            var (fw, fh) = o.Portrait ? (fitPaper.Height, fitPaper.Width) : (fitPaper.Width, fitPaper.Height);
            var fitBox = target ?? FrameBox(doc) ?? Extents(doc);
            var fitK = Math.Max(fitBox.Width / (fw - 2 * FitMargin), fitBox.Height / (fh - 2 * FitMargin));
            if (!(fitK > 1e-12)) fitK = doc.ScaleDenominator / 1000;
            return new Layout(fitPaper.Name, fw, fh, fitBox.Center - new Vec2(fw, fh) / 2 * fitK, fitK);
        }

        var scale = o.ScaleDenominator is double s && s > 0 ? s : doc.ScaleDenominator;
        var k = scale / 1000;
        // 範囲・用紙を指定しない & 図面の縮尺のままなら、SFC と同じ選び方
        if (target == null && o.Paper == null && !o.Portrait && Math.Abs(scale - doc.ScaleDenominator) < 1e-9)
        {
            var (p, w, h, origin) = SfcWriter.Layout(doc);
            return new Layout(p?.Name ?? "自由", w, h, origin, k);
        }
        var ext = target ?? Extents(doc);
        PaperSize? paper = o.Paper != null ? PaperSize.All.FirstOrDefault(x => x.Name == o.Paper) : null;
        double pw, ph;
        if (paper != null)
        {
            (pw, ph) = o.Portrait ? (paper.Height, paper.Width) : (paper.Width, paper.Height);
        }
        else
        {
            // 図形が余白付きで収まる最小の A 判
            var needW = ext.Width / k + 2 * Margin;
            var needH = ext.Height / k + 2 * Margin;
            var fit = PaperSize.All.Select(x => o.Portrait ? new PaperSize(x.Name, x.Height, x.Width) : x)
                .Where(x => x.Width >= needW && x.Height >= needH).OrderBy(x => x.Width * x.Height).FirstOrDefault();
            paper = fit;
            (pw, ph) = fit != null ? (fit.Width, fit.Height) : (Math.Ceiling(needW), Math.Ceiling(needH));
        }
        // 範囲 (指定が無ければ図枠、それも無ければ図形全体) を用紙の中央に置く
        var box = target ?? FrameBox(doc) ?? ext;
        var center = box.IsEmpty ? Vec2.Zero : box.Center;
        return new Layout(paper?.Name ?? "自由", pw, ph, center - new Vec2(pw, ph) / 2 * k, k);
    }

    /// <summary>印刷する範囲 (範囲の指定、無ければ選択した図形の範囲)。指定が無ければ null。</summary>
    public static Box2? TargetBox(CadDocument doc, PdfOptions o)
    {
        if (o.Region is { IsEmpty: false } r) return r;
        if (o.Only == null) return null;
        var box = o.Only.Where(doc.IsVisible).Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
        return box.IsEmpty ? null : box;
    }

    private static Box2 Extents(CadDocument doc)
    {
        var ext = doc.Entities.Where(doc.IsVisible).Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
        return ext.IsEmpty ? new Box2(Vec2.Zero, Vec2.Zero) : ext;
    }

    private static Box2? FrameBox(CadDocument doc) => doc.Entities.OfType<PolylineEntity>()
        .Where(p => p.Layer.Name == DrawingFrame.LayerFrame && p.Points.Count >= 4 && doc.IsVisible(p))
        .MaxBy(p => p.Bounds.Width * p.Bounds.Height)?.Bounds;

    /// <summary>PDF に印刷して、使った用紙の配置を返す。</summary>
    public static Layout Export(CadDocument doc, string path, PdfOptions o)
    {
        var layout = ComputeLayout(doc, o);
        using (var stream = File.Create(path))
        {
            var metadata = new SKDocumentPdfMetadata
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Creator = "CivilCad",
                Producer = "CivilCad",
                RasterDpi = 300,
            };
            using var pdf = SKDocument.CreatePdf(stream, metadata);
            var canvas = pdf.BeginPage((float)(layout.Width * PtPerMm), (float)(layout.Height * PtPerMm));
            Render(doc, canvas, layout, o);
            pdf.EndPage();
            pdf.Close();
        }
        return layout;
    }

    /// <summary>用紙 1 枚を描く (canvas の単位は pt、左上が原点)。PDF のほか、確認用に画像にも描ける。</summary>
    public static void Render(CadDocument doc, SKCanvas canvas, Layout layout, PdfOptions o) => new Painter(doc, canvas, layout, o).Draw();

    /// <summary>用紙の大きさ (pt)。</summary>
    public static SKSize PageSize(Layout layout) => new((float)(layout.Width * PtPerMm), (float)(layout.Height * PtPerMm));

    private sealed class Painter(CadDocument doc, SKCanvas canvas, Layout layout, PdfOptions o)
    {
        private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("MS Gothic") ?? SKTypeface.FromFamilyName("Yu Gothic") ?? SKTypeface.Default;

        /// <summary>図面 (m) → PDF (pt、左上が原点で y は下向き)。</summary>
        private SKPoint P(Vec2 w)
        {
            var mm = (w - layout.Origin) / layout.MetersPerMm;
            return new SKPoint((float)(mm.X * PtPerMm), (float)((layout.Height - mm.Y) * PtPerMm));
        }

        private float Len(double meters) => (float)(meters / layout.MetersPerMm * PtPerMm);

        public void Draw()
        {
            canvas.Clear(SKColors.White);
            // 範囲を指定したときは、範囲の外を印刷しない
            if (o.Region is { IsEmpty: false } r)
            {
                var (a, b) = (P(r.Min), P(r.Max));
                canvas.ClipRect(new SKRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
            }
            // プリセット (記号) は線・塗り・円・文字に展開する
            var visible = (o.Only ?? doc.Entities).Where(doc.IsVisible).SelectMany(e => e is SymbolEntity s ? s.Explode() : [e]).ToList();
            // 画像は一番下
            foreach (var img in visible.OfType<ImageEntity>().Where(i => !i.InFront)) DrawImage(img);
            // 塗りは線の下に描く (画面と同じ)
            foreach (var e in visible.Where(IsFill)) DrawFill(e);
            foreach (var e in visible.Where(e => !IsFill(e))) DrawEntity(e);
            // 最前面にした画像は線・文字の上
            foreach (var img in visible.OfType<ImageEntity>().Where(i => i.InFront)) DrawImage(img);
        }

        /// <summary>画像 (オルソ画像・PDF のページ)。用紙上で 300dpi 相当の画素で読む。読めなければ描かない。</summary>
        private void DrawImage(ImageEntity img)
        {
            using var bmp = ImageRaster.Load(img, (int)Math.Ceiling(img.Width / layout.MetersPerMm / 25.4 * 300));
            if (bmp == null) return;
            // 画素 (x, y) (左上が原点) → 用紙: 左上の角 + U 方向 × x / 幅 + (−V) 方向 × y / 高さ
            var topLeft = P(img.Origin + img.V);
            var right = P(img.Origin + img.U + img.V) - topLeft;
            var down = P(img.Origin) - topLeft;
            var m = new SKMatrix(right.X / bmp.Width, down.X / bmp.Height, topLeft.X, right.Y / bmp.Width, down.Y / bmp.Height, topLeft.Y, 0, 0, 1);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * (1 - Math.Clamp(img.TransparencyPercent, 0, 100) / 100))) };
            if (o.Monochrome) paint.ColorFilter = SKColorFilter.CreateColorMatrix([0.299f, 0.587f, 0.114f, 0, 0, 0.299f, 0.587f, 0.114f, 0, 0, 0.299f, 0.587f, 0.114f, 0, 0, 0, 0, 0, 1, 0]);
            using var image = SKImage.FromBitmap(bmp);
            canvas.Save();
            // トリミングした範囲だけ
            var clip = new SKPathBuilder();
            var corners = img.Corners;
            clip.MoveTo(P(corners[0]));
            foreach (var c in corners.Skip(1)) clip.LineTo(P(c));
            clip.Close();
            using (var clipPath = clip.Detach()) canvas.ClipPath(clipPath, antialias: true);
            canvas.Concat(in m);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
            canvas.Restore();
        }

        private static bool IsFill(Entity e) => e is HatchEntity { Solid: true } or PolylineEntity { Filled: true };

        private SKColor Color(Entity e, bool fill = false)
        {
            var c = e.EffectiveColor;
            // 画面の白 (暗い背景用の既定色) は用紙では黒
            var (r, g, b) = c == RgbColor.White ? ((byte)0, (byte)0, (byte)0) : (c.R, c.G, c.B);
            if (!o.Monochrome) return new SKColor(r, g, b);
            if (!fill) return SKColors.Black;
            var y = (byte)Math.Clamp(0.299 * r + 0.587 * g + 0.114 * b, 0, 230); // 塗りは明るさを残した灰色
            return new SKColor(y, y, y);
        }

        private SKPaint Stroke(Entity e) => new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = Color(e),
            StrokeWidth = (float)Math.Max((e.LineWeight ?? o.DefaultLineWeightMm) * PtPerMm, 0.1),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        private void DrawFill(Entity e)
        {
            var builder = new SKPathBuilder();
            IEnumerable<IReadOnlyList<Vec2>> loops = e switch
            {
                HatchEntity h => h.Loops,
                PolylineEntity p => [p.Points],
                _ => [],
            };
            foreach (var loop in loops)
            {
                if (loop.Count < 3) continue;
                builder.MoveTo(P(loop[0]));
                foreach (var p in loop.Skip(1)) builder.LineTo(P(p));
                builder.Close();
            }
            var color = Color(e, fill: true);
            var opacity = e is HatchEntity he ? he.Opacity : 1;
            using var path = builder.Detach();
            path.FillType = SKPathFillType.EvenOdd; // 中抜き (2 つ目以降の輪郭) は塗らない
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color.WithAlpha((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1))) };
            canvas.DrawPath(path, paint);
        }

        private void DrawEntity(Entity e)
        {
            using var paint = Stroke(e);
            switch (e)
            {
                case LineEntity l:
                    canvas.DrawLine(P(l.Start), P(l.End), paint);
                    break;
                case PolylineEntity pl when pl.Points.Count >= 2:
                {
                    var builder = new SKPathBuilder();
                    builder.MoveTo(P(pl.Points[0]));
                    foreach (var p in pl.Points.Skip(1)) builder.LineTo(P(p));
                    if (pl.IsClosed) builder.Close();
                    using var path = builder.Detach();
                    canvas.DrawPath(path, paint);
                    break;
                }
                case CircleEntity c:
                    canvas.DrawCircle(P(c.Center), Len(c.Radius), paint);
                    break;
                case PointEntity pt:
                {
                    // 点は用紙上 φ0.5mm (線幅が太ければ線幅の 2 倍) の黒丸
                    var d = Math.Max(PointDiameterMm, 2 * (e.LineWeight ?? 0));
                    using var dot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Color(e) };
                    canvas.DrawCircle(P(pt.Position), (float)(d / 2 * PtPerMm), dot);
                    break;
                }
                case ArcEntity a:
                {
                    var c = P(a.Center);
                    var r = Len(a.Radius);
                    var builder = new SKPathBuilder();
                    // 用紙は y が下向きなので、反時計回りの角度は符号が逆になる
                    builder.ArcTo(new SKRect(c.X - r, c.Y - r, c.X + r, c.Y + r), (float)(-a.StartAngle * 180 / Math.PI), (float)(-a.Sweep * 180 / Math.PI), true);
                    using var path = builder.Detach();
                    canvas.DrawPath(path, paint);
                    break;
                }
                case HatchEntity h:
                    foreach (var (a, b) in h.Lines) canvas.DrawLine(P(a), P(b), paint);
                    break;
                case TextEntity t:
                    DrawText(t);
                    break;
            }
        }

        /// <summary>文字: 画面と同じく、全角文字の枠 (高さ = 文字高) の下端を配置の基準にする。</summary>
        private void DrawText(TextEntity t)
        {
            if (string.IsNullOrEmpty(t.Text)) return;
            // 画面で一定の文字は、用紙上 1px = 0.26mm の大きさで印刷する (保存と同じ)
            var heightM = t.ScreenHeightPx is double px ? px * TextEntity.MmPerPixel * layout.MetersPerMm : t.Height;
            var em = Len(heightM);
            if (em <= 0.01f) return;
            using var font = new SKFont(_typeface, em) { Subpixel = true, Edging = SKFontEdging.Antialias };
            using var paint = new SKPaint { IsAntialias = true, Color = Color(t), Style = SKPaintStyle.Fill };
            var natural = Math.Max(font.MeasureText(t.Text), 1e-3f);
            // 画面と同じ幅: フィット幅か、幅係数
            var widthM = t.FitWidth is double fit ? fit * (heightM / Math.Max(t.Height, 1e-12)) : natural * t.WidthFactor / Len(1);
            var xScale = t.FitWidth != null ? (float)(Len(widthM) / natural) : (float)t.WidthFactor;
            var copy = (TextEntity)t.Clone();
            copy.Height = heightM;
            var start = P(copy.BaselineStart(widthM));
            font.GetFontMetrics(out var metrics);
            canvas.Save();
            canvas.Translate(start.X, start.Y);
            canvas.RotateDegrees((float)(-t.Rotation * 180 / Math.PI));
            canvas.Scale(xScale, 1);
            if (o.EmbedFonts)
            {
                canvas.DrawText(t.Text, 0, -metrics.Descent, SKTextAlign.Left, font, paint);
            }
            else
            {
                // 文字の形 (アウトライン) で描く。日本語のフォントは丸ごと埋め込まれて PDF が大きくなるため
                using var outline = font.GetTextPath(t.Text, new SKPoint(0, -metrics.Descent));
                canvas.DrawPath(outline, paint);
            }
            canvas.Restore();
        }
    }
}
