using System.Text.Json.Serialization;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Drawing;

public sealed record PaperSize(string Name, double Width, double Height)
{
    public static IReadOnlyList<PaperSize> All { get; } =
    [
        new("A0", 1189, 841),
        new("A1", 841, 594),
        new("A2", 594, 420),
        new("A3", 420, 297),
        new("A4", 297, 210),
    ];

    public override string ToString() => $"{Name} 横 ({Width}×{Height})";
}

/// <summary>図枠 (外枠 + 表題欄) の設定。寸法は用紙上の mm。前回の入力を設定ファイルに残す。</summary>
public sealed class DrawingFrameOptions
{
    public string Paper { get; set; } = "A1";
    public double ScaleDenominator { get; set; } = 500;
    public double Margin { get; set; } = 20;
    public bool IncludeTitleBlock { get; set; } = true;

    public string ProjectName { get; set; } = "";
    public string DrawingName { get; set; } = "";
    public string Date { get; set; } = "令和　　年　　月";
    public string DrawingNumber { get; set; } = "";
    public string Company { get; set; } = "";
    public string Client { get; set; } = "";

    [JsonIgnore]
    public PaperSize PaperSize => PaperSize.All.FirstOrDefault(p => p.Name == Paper) ?? PaperSize.All[1];
    [JsonIgnore]
    public string ScaleText => $"1:{ScaleDenominator:0.##}";

    /// <summary>用紙 1mm が図面上で何 m になるか (1/500 なら 0.5)。</summary>
    [JsonIgnore]
    public double MetersPerPaperMm => ScaleDenominator / 1000;

    public DrawingFrameOptions Clone() => (DrawingFrameOptions)MemberwiseClone();
}

/// <summary>
/// 国交省の CAD 製図基準に沿った図枠を作る。
/// 外枠: 用紙の端から Margin 内側 (太線 1.4mm, レイヤ D-TTL)。
/// 表題欄: 外枠の右下に 100×60mm、10mm × 6 段 (工事名/図面名/作成年月日/縮尺・図面番号/会社名/事業者名)。
/// </summary>
public static class DrawingFrame
{
    public const string Tag = "FRAME";
    public const string LayerFrame = "D-TTL";
    public const string LayerTitleFrame = "D-TTL-FRAM";
    public const string LayerTitleLine = "D-TTL-LINE";
    public const string LayerTitleText = "D-TTL-TXT";

    public const double TitleWidth = 100;
    public const double TitleHeight = 60;
    private const double RowHeight = 10;
    private const double LabelWidth = 20;
    private const double TextHeight = 3.5;

    /// <summary>外枠の大きさ (m)。</summary>
    public static (double Width, double Height) FrameSize(DrawingFrameOptions o) =>
        ((o.PaperSize.Width - 2 * o.Margin) * o.MetersPerPaperMm, (o.PaperSize.Height - 2 * o.Margin) * o.MetersPerPaperMm);

    /// <summary>「図形が収まる縮尺」で選ぶ縮尺 (分母)。</summary>
    public static readonly double[] StandardScales =
        [1, 2, 5, 10, 20, 25, 50, 100, 150, 200, 250, 300, 400, 500, 600, 1000, 1500, 2000, 2500, 3000, 5000, 10000, 20000, 25000, 50000];

    /// <summary>図形と外枠・表題欄の間にあける用紙上の余裕 (mm)。</summary>
    public const double FitPadding = 10;

    /// <summary>図枠の図形を除いた、表示中のレイヤの図形の範囲。図形が無ければ null。</summary>
    public static Box2? DrawingExtents(CadDocument doc)
    {
        var box = doc.Entities.Where(e => e.Tag != Tag && doc.IsVisible(e)).Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
        return box.IsEmpty ? null : box;
    }

    /// <summary>
    /// 外枠の中で図形を置ける場所 (外枠の左下からの用紙 mm)。表題欄があれば、表題欄の上 (横いっぱい) と左 (縦いっぱい) の 2 つ。
    /// </summary>
    private static IEnumerable<(Vec2 Min, Vec2 Max)> FreeAreas(DrawingFrameOptions o)
    {
        var fw = o.PaperSize.Width - 2 * o.Margin;
        var fh = o.PaperSize.Height - 2 * o.Margin;
        var pad = new Vec2(FitPadding, FitPadding);
        if (!o.IncludeTitleBlock)
        {
            yield return (pad, new Vec2(fw, fh) - pad);
            yield break;
        }
        yield return (new Vec2(0, TitleHeight) + pad, new Vec2(fw, fh) - pad);
        yield return (pad, new Vec2(fw - TitleWidth, fh) - pad);
    }

    /// <summary>縮尺 (分母) をこれ以上にすれば、図形が外枠の中 (表題欄を除く) に収まる。</summary>
    private static double RequiredScale(DrawingFrameOptions o, Box2 ext) => FreeAreas(o)
        .Where(a => a.Max.X > a.Min.X && a.Max.Y > a.Min.Y)
        .Select(a => Math.Max(ext.Width * 1000 / (a.Max.X - a.Min.X), ext.Height * 1000 / (a.Max.Y - a.Min.Y)))
        .DefaultIfEmpty(double.PositiveInfinity).Min();

    /// <summary>図形が外枠の中 (表題欄を除く) に収まる、いちばん大きく描ける標準の縮尺 (分母)。</summary>
    public static double FitScale(DrawingFrameOptions o, Box2 ext)
    {
        var need = RequiredScale(o, ext);
        return StandardScales.FirstOrDefault(s => s >= need * (1 - 1e-9), Math.Ceiling(need / 10000) * 10000);
    }

    /// <summary>今の縮尺で図形が外枠の中 (表題欄を除く) に収まるか。</summary>
    public static bool Fits(DrawingFrameOptions o, Box2 ext) => RequiredScale(o, ext) <= o.ScaleDenominator * (1 + 1e-9);

    /// <summary>図形が外枠の中 (表題欄を除いた場所) の中央に来るような、外枠の左下の位置。</summary>
    public static Vec2 CenteredOrigin(DrawingFrameOptions o, Box2 ext)
    {
        var k = o.MetersPerPaperMm;
        // 図形を置ける場所のうち、余裕の大きい方
        var area = FreeAreas(o)
            .OrderByDescending(a => Math.Min((a.Max.X - a.Min.X) * k - ext.Width, (a.Max.Y - a.Min.Y) * k - ext.Height))
            .First();
        return ext.Center - (area.Min + area.Max) / 2 * k;
    }

    /// <summary>外枠の左下を origin に置いたときの図形 (図面座標, m)。</summary>
    public static List<Entity> Build(CadDocument doc, DrawingFrameOptions o, Vec2 origin)
    {
        if (o.ScaleDenominator <= 0) throw new ArgumentException("縮尺は正の値にしてください。");
        var k = o.MetersPerPaperMm;
        var (w, h) = FrameSize(o);
        Vec2 P(double xMm, double yMm) => origin + new Vec2(xMm * k, yMm * k);

        var frameLayer = doc.GetOrCreateLayer(LayerFrame, RgbColor.White);
        var list = new List<Entity>
        {
            new PolylineEntity(frameLayer, [origin, origin + new Vec2(w, 0), origin + new Vec2(w, h), origin + new Vec2(0, h)], true)
            {
                LineWeight = 1.4,
            },
        };

        if (o.IncludeTitleBlock)
        {
            var frameMmW = o.PaperSize.Width - 2 * o.Margin;
            if (frameMmW < TitleWidth || o.PaperSize.Height - 2 * o.Margin < TitleHeight)
                throw new ArgumentException("用紙が小さく、表題欄が入りません。");
            AddTitleBlock(doc, o, list, (x, y) => P(frameMmW - TitleWidth + x, y), k);
        }

        foreach (var e in list) e.Tag = Tag;
        return list;
    }

    private static void AddTitleBlock(CadDocument doc, DrawingFrameOptions o, List<Entity> list, Func<double, double, Vec2> t, double k)
    {
        var frame = doc.GetOrCreateLayer(LayerTitleFrame, RgbColor.White);
        var line = doc.GetOrCreateLayer(LayerTitleLine, RgbColor.White);
        var text = doc.GetOrCreateLayer(LayerTitleText, RgbColor.White);

        list.Add(new PolylineEntity(frame, [t(0, 0), t(TitleWidth, 0), t(TitleWidth, TitleHeight), t(0, TitleHeight)], true) { LineWeight = 0.5 });

        for (var y = RowHeight; y < TitleHeight; y += RowHeight)
            list.Add(new LineEntity(line, t(0, y), t(TitleWidth, y)) { LineWeight = 0.13 });
        list.Add(new LineEntity(line, t(LabelWidth, 0), t(LabelWidth, TitleHeight)) { LineWeight = 0.13 });
        // 縮尺の段: 縮尺 | 値 | 図面番号 | 値
        list.Add(new LineEntity(line, t(50, 20), t(50, 30)) { LineWeight = 0.13 });
        list.Add(new LineEntity(line, t(70, 20), t(70, 30)) { LineWeight = 0.13 });

        void Text(double cx, double cy, string s, double maxWidthMm)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            var e = new TextEntity(text, t(cx, cy), s, TextHeight * k)
            {
                HAlign = TextHAlign.Center,
                VAlign = TextVAlign.Middle,
            };
            // 欄に収まらない長さなら文字幅を詰める
            var width = e.ApproxWidth / k;
            if (width > maxWidthMm) e.WidthFactor = maxWidthMm / width;
            list.Add(e);
        }

        const double valueCenter = LabelWidth + (TitleWidth - LabelWidth) / 2;
        const double valueWidth = TitleWidth - LabelWidth - 4;
        (string Label, string Value)[] rows =
        [
            ("事業者名", o.Client),
            ("会 社 名", o.Company),
            ("縮    尺", ""),
            ("作成年月日", o.Date),
            ("図 面 名", o.DrawingName),
            ("工 事 名", o.ProjectName),
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            var cy = RowHeight * i + RowHeight / 2;
            Text(LabelWidth / 2, cy, rows[i].Label, LabelWidth - 2);
            Text(valueCenter, cy, rows[i].Value, valueWidth);
        }
        Text(35, 25, o.ScaleText, 28);
        Text(60, 25, "図面番号", 18);
        Text(85, 25, o.DrawingNumber, 28);
    }
}
