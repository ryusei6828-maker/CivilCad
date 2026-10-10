using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

/// <summary>引き出し線の先端の形。</summary>
public enum LeaderTip { Arrow, Dot, None }

/// <summary>
/// 引き出し線・旗揚げ。1 つの図形として選択・移動・回転・削除でき、文字もまとめて変えられる。
/// Points[0] が先端 (矢印・黒丸の付く点)、残りが折れ点。
/// ・引き出し線 (IsFlag = false): Points を結ぶ線。文字があれば最後の点の先 (線の向きの側) に横書きで置く
/// ・旗揚げ (IsFlag = true): Points を結ぶ線の最後の点から、先端と反対側へ水平な線 (旗竿) を文字の幅だけ引き、
///   その上に文字を置く (複数行は上へ積む。1 行目が一番上)
/// 文字・旗竿は回転しても常に水平。大きさ (TextHeight・TipSize) は図面上の m。
/// </summary>
public sealed class LeaderEntity(Layer layer, IEnumerable<Vec2> points, bool isFlag, string text, double textHeight, double tipSize, LeaderTip tip = LeaderTip.Arrow)
    : Entity(layer)
{
    public List<Vec2> Points { get; } = [.. points];
    public bool IsFlag { get; set; } = isFlag;

    /// <summary>文字 (改行で複数行)。空なら線だけ。</summary>
    public string Text { get; set; } = text;

    public double TextHeight { get; set; } = textHeight;
    public double TipSize { get; set; } = tipSize;
    public LeaderTip Tip { get; set; } = tip;

    public string[] Lines => Text.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToArray();

    private double Gap => TextHeight * 0.4;
    private double LinePitch => TextHeight * 1.6;

    /// <summary>旗竿が右へ伸びるか (先端が折れ点より左にあれば右へ)。</summary>
    private bool ShelfToRight => Points.Count < 2 || Points[^1].X >= Points[^2].X;

    private static double TextWidth(string s, double h) => s.Sum(c => c < 0x80 || c is >= '｡' and <= 'ﾟ' ? 0.5 : 1.0) * h;

    /// <summary>旗竿の終わりの点 (旗揚げのみ)。</summary>
    public Vec2 ShelfEnd
    {
        get
        {
            var last = Points[^1];
            var lines = Lines;
            var w = lines.Length == 0 ? TextHeight * 4 : lines.Max(l => TextWidth(l, TextHeight));
            var len = w + Gap * 2;
            return last + new Vec2(ShelfToRight ? len : -len, 0);
        }
    }

    /// <summary>線・先端・文字に展開する (表示と保存に使う)。</summary>
    public IEnumerable<Entity> Explode()
    {
        if (Points.Count == 0) yield break;
        var line = new List<Vec2>(Points);
        if (IsFlag) line.Add(ShelfEnd);
        if (line.Count >= 2) yield return CopyPropertiesTo(new PolylineEntity(Layer, line, false));

        // 先端: 最初の線の向きに合わせた矢印 / 黒丸
        if (line.Count >= 2 && Tip != LeaderTip.None && TipSize > 0)
        {
            var p0 = line[0];
            var dir = (line[1] - p0).Normalized();
            if (Tip == LeaderTip.Arrow)
            {
                var back = p0 + dir * TipSize;
                var side = dir.LeftNormal * (TipSize * 0.18);
                yield return CopyPropertiesTo(new HatchEntity(Layer, [[p0, back + side, back - side]], [], solid: true));
            }
            else
            {
                var r = TipSize * 0.25;
                yield return CopyPropertiesTo(new HatchEntity(Layer,
                    [Enumerable.Range(0, 24).Select(i => p0 + Vec2.FromAngle(i * Math.PI / 12) * r)], [], solid: true));
            }
        }

        var lines = Lines;
        if (lines.Length == 0) yield break;
        var last = Points[^1];
        if (IsFlag)
        {
            // 旗竿の上に、下の行から積む
            var right = ShelfToRight;
            for (var i = 0; i < lines.Length; i++)
            {
                var row = lines.Length - 1 - i; // 下から何行目か
                var pos = last + new Vec2(right ? Gap : -Gap, Gap + row * LinePitch);
                yield return CopyPropertiesTo(new TextEntity(Layer, pos, lines[i], TextHeight)
                {
                    HAlign = right ? TextHAlign.Left : TextHAlign.Right,
                    VAlign = TextVAlign.Bottom,
                });
            }
        }
        else
        {
            // 最後の点の先に、線の向きの側へ (複数行は上下中央にそろえる)
            var toRight = ShelfToRight;
            var top = (lines.Length - 1) * LinePitch / 2;
            for (var i = 0; i < lines.Length; i++)
            {
                var pos = last + new Vec2(toRight ? Gap : -Gap, top - i * LinePitch);
                yield return CopyPropertiesTo(new TextEntity(Layer, pos, lines[i], TextHeight)
                {
                    HAlign = toRight ? TextHAlign.Left : TextHAlign.Right,
                    VAlign = TextVAlign.Middle,
                });
            }
        }
    }

    public override IEnumerable<Entity> ExplodeForOutput() => Explode();

    public override Box2 Bounds => Explode().Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));

    public override double DistanceTo(Vec2 p) => Explode().Min(e => e.DistanceTo(p));

    public override bool IntersectsBox(Box2 box) => Explode().Any(e => e.IntersectsBox(box));

    public override void Translate(Vec2 delta)
    {
        for (var i = 0; i < Points.Count; i++) Points[i] += delta;
    }

    /// <summary>折れ点だけを回す (旗竿と文字は水平のまま)。</summary>
    public override void Rotate(Vec2 origin, double rad)
    {
        for (var i = 0; i < Points.Count; i++) Points[i] = Points[i].RotateAround(origin, rad);
    }

    public override void Scale(Vec2 origin, double k)
    {
        for (var i = 0; i < Points.Count; i++) Points[i] = ScalePoint(Points[i], origin, k);
        TextHeight *= k;
        TipSize *= k;
    }

    public override IEnumerable<Curve> Curves => Explode().OfType<PolylineEntity>().SelectMany(p => p.Curves);

    public override Entity Clone() => CopyPropertiesTo(new LeaderEntity(Layer, Points, IsFlag, Text, TextHeight, TipSize, Tip));

    public override IEnumerable<SnapPoint> SnapPoints =>
        Points.Select((p, i) => new SnapPoint(p, i == 0 ? SnapKind.Endpoint : SnapKind.Node))
            .Concat(IsFlag && Points.Count > 0 ? [new SnapPoint(ShelfEnd, SnapKind.Endpoint)] : []);
}
