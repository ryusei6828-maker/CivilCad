using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

public enum TextHAlign { Left, Center, Right }

/// <summary>縦方向の基準。Baseline/Bottom = 文字の下端、Middle = 中央、Top = 上端 (高さ H の位置)。</summary>
public enum TextVAlign { Baseline, Bottom, Middle, Top }

/// <summary>
/// 文字。Position は配置基準点 (HAlign/VAlign で決まる点。既定は左下)、
/// Height は実寸 (m)、Rotation はラジアン、WidthFactor は幅係数。
/// FitWidth が指定されていれば文字列全体をその幅に合わせる (DXF の「両端揃え/フィット」)。
/// </summary>
public sealed class TextEntity(Layer layer, Vec2 position, string text, double height, double rotation = 0) : Entity(layer)
{
    public Vec2 Position { get; set; } = position;
    public string Text { get; set; } = text;
    public double Height { get; set; } = height;
    public double Rotation { get; set; } = rotation;
    public TextHAlign HAlign { get; set; }
    public TextVAlign VAlign { get; set; }
    public double WidthFactor { get; set; } = 1;
    public double? FitWidth { get; set; }

    /// <summary>既定のフォント (CAD の図面で一般的な等幅のゴシック)。</summary>
    public const string DefaultFontName = "ＭＳ ゴシック";

    /// <summary>フォント名 (「ＭＳ 明朝」など)。null なら既定 (<see cref="DefaultFontName"/>)。</summary>
    public string? FontName { get; set; }

    public string EffectiveFontName => string.IsNullOrWhiteSpace(FontName) ? DefaultFontName : FontName;

    /// <summary>
    /// 画面上で一定の大きさで表示する文字の高さ (ピクセル)。null なら普通の文字 (Height が図面上の高さ)。
    /// 設定されているときの Height は、いまの表示倍率での高さ (表示側が <see cref="SyncScreenHeight"/> で合わせる)。
    /// </summary>
    public double? ScreenHeightPx { get; set; }

    /// <summary>1 ピクセル = 25.4 / 96 mm (保存するときは用紙上のこの大きさにする)。</summary>
    public const double MmPerPixel = 25.4 / 96;

    /// <summary>画面で一定の文字の Height を、表示倍率 (1m = pixelsPerMeter ピクセル) に合わせる。</summary>
    public void SyncScreenHeight(double pixelsPerMeter)
    {
        if (ScreenHeightPx is double px && pixelsPerMeter > 0) Height = px / pixelsPerMeter;
    }

    /// <summary>文字幅の概算 (等幅フォント想定: 全角 1.0H、半角 0.5H)。</summary>
    public double ApproxWidth => FitWidth ?? Text.Sum(c => c < 0x80 || c is >= '｡' and <= 'ﾟ' ? 0.5 : 1.0) * Height * WidthFactor;

    /// <summary>配置基準を考慮した文字列の左下 (ベースライン左端)。width は実際の文字列幅。</summary>
    public Vec2 BaselineStart(double width)
    {
        var dx = HAlign switch { TextHAlign.Center => -width / 2, TextHAlign.Right => -width, _ => 0 };
        var dy = VAlign switch { TextVAlign.Top => -Height, TextVAlign.Middle => -Height / 2, _ => 0 };
        var u = Vec2.FromAngle(Rotation);
        return Position + u * dx + u.LeftNormal * dy;
    }

    private IEnumerable<Vec2> Corners
    {
        get
        {
            var u = Vec2.FromAngle(Rotation);
            var v = u.LeftNormal;
            var w = ApproxWidth;
            var p = BaselineStart(w);
            yield return p;
            yield return p + u * w;
            yield return p + u * w + v * Height;
            yield return p + v * Height;
        }
    }

    public override Box2 Bounds => Box2.FromPoints(Corners);

    /// <summary>
    /// 文字の枠 (テキストボックス) の点: 4 隅 (端点)・各辺の中点 (中点)・中心 (中心)。
    /// ふだんはスナップしない (文字の位置だけ) が、回転コマンドでは回す中心・方向点に使えるようにする。
    /// </summary>
    public IEnumerable<SnapPoint> TextBoxSnapPoints
    {
        get
        {
            var c = Corners.ToArray();
            for (var i = 0; i < 4; i++)
            {
                yield return new SnapPoint(c[i], SnapKind.Endpoint);
                yield return new SnapPoint((c[i] + c[(i + 1) % 4]) / 2, SnapKind.Midpoint);
            }
            yield return new SnapPoint((c[0] + c[2]) / 2, SnapKind.Center);
        }
    }

    public override double DistanceTo(Vec2 p)
    {
        // 文字の局所座標で矩形との距離を求める
        var w = ApproxWidth;
        var d = (p - BaselineStart(w)).Rotate(-Rotation);
        var dx = Math.Max(0, Math.Max(-d.X, d.X - w));
        var dy = Math.Max(0, Math.Max(-d.Y, d.Y - Height));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override void Translate(Vec2 delta) => Position += delta;

    public override void Rotate(Vec2 origin, double rad)
    {
        Position = Position.RotateAround(origin, rad);
        Rotation += rad;
    }

    public override void Scale(Vec2 origin, double k)
    {
        Position = ScalePoint(Position, origin, k);
        Height *= k;
        if (ScreenHeightPx is double px) ScreenHeightPx = px * k; // 画面で一定の文字も同じ割合で
        if (FitWidth is double fit) FitWidth = fit * k;
    }

    public override Entity Clone() => CopyPropertiesTo(new TextEntity(Layer, Position, Text, Height, Rotation)
    {
        HAlign = HAlign,
        VAlign = VAlign,
        WidthFactor = WidthFactor,
        FitWidth = FitWidth,
        ScreenHeightPx = ScreenHeightPx,
        FontName = FontName,
    });

    public override IEnumerable<SnapPoint> SnapPoints => [new(Position, SnapKind.Node)];
}
