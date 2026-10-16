using System.Globalization;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>登録座標に描くマークの種類。</summary>
public enum PointMarkKind
{
    /// <summary>十字 + 〇。</summary>
    CrossCircle,
    /// <summary>点 (黒丸) + 〇。</summary>
    DotCircle,
    /// <summary>十字。</summary>
    Cross,
    /// <summary>×。</summary>
    XMark,
    /// <summary>〇。</summary>
    Circle,
    /// <summary>◎ (二重丸)。</summary>
    DoubleCircle,
    /// <summary>△ + 点 (基準点など)。</summary>
    TrianglePoint,
    /// <summary>点 (黒丸) だけ。</summary>
    Dot,
}

/// <summary>座標の描画の設定 (大きさ・文字高は用紙上の mm。次の描画でも同じ設定を使う)。</summary>
public sealed class PointMarkSettings
{
    public PointMarkKind Kind { get; set; } = PointMarkKind.CrossCircle;

    /// <summary>マークの大きさ (〇の直径、用紙上の mm)。</summary>
    public double SizeMm { get; set; } = 3;

    /// <summary>点名も描く。</summary>
    public bool DrawName { get; set; } = true;

    /// <summary>標高 (H=…) も描く (標高のある点だけ)。</summary>
    public bool DrawElevation { get; set; }

    /// <summary>点名・標高の文字高 (用紙上の mm)。</summary>
    public double TextHeightMm { get; set; } = 2.5;

    public static PointMarkSettings Current { get; } = new();
}

/// <summary>
/// 座標の描画 (CD): 座標管理に登録した点に、マーク (十字 + 〇 など) と点名を線・円・文字で描く。
/// 印刷・DXF / SFC にもそのまま出る。描いた図形には点名ごとのタグを付け、同じ点を描き直すと前のマークを置き換える。
/// </summary>
public static class CoordinateMarks
{
    public const string MarkLayer = "座標-マーク";
    public const string NameLayer = "座標-マーク点名";
    public const string TagPrefix = "PTMARK:";

    public static string Label(PointMarkKind kind) => kind switch
    {
        PointMarkKind.CrossCircle => "十字＋〇",
        PointMarkKind.DotCircle => "点＋〇",
        PointMarkKind.Cross => "十字",
        PointMarkKind.XMark => "×",
        PointMarkKind.Circle => "〇",
        PointMarkKind.DoubleCircle => "◎ (二重丸)",
        PointMarkKind.TrianglePoint => "△＋点",
        PointMarkKind.Dot => "点 (黒丸)",
        _ => kind.ToString(),
    };

    public static string TagOf(PointEntity p) => TagPrefix + p.Name;

    /// <summary>この点に描いてあるマーク・点名。</summary>
    public static List<Entity> Existing(CadDocument doc, IEnumerable<PointEntity> points)
    {
        var tags = points.Select(TagOf).ToHashSet();
        return [.. doc.Entities.Where(e => e.Tag != null && tags.Contains(e.Tag))];
    }

    /// <summary>点を描く (前に描いたその点のマークは置き換える)。元に戻せるコマンドを返す。</summary>
    public static IUndoableCommand CreateDrawCommand(CadDocument doc, IReadOnlyCollection<PointEntity> points, PointMarkSettings s)
    {
        var old = Existing(doc, points);
        var markLayer = doc.GetOrCreateLayer(MarkLayer, RgbColor.Cyan);
        var nameLayer = doc.GetOrCreateLayer(NameLayer, RgbColor.White);
        var made = points.SelectMany(p => Build(doc, p, s, markLayer, nameLayer)).ToList();
        return new CompositeCommand($"座標の描画 ({points.Count} 点)", new RemoveEntitiesCommand(old), new AddEntitiesCommand(made));
    }

    /// <summary>描いたマーク・点名を消すコマンド (無ければ null)。</summary>
    public static IUndoableCommand? CreateEraseCommand(CadDocument doc, IEnumerable<PointEntity> points)
    {
        var old = Existing(doc, points);
        return old.Count == 0 ? null : new RemoveEntitiesCommand(old, $"座標のマークを消す ({old.Count} 個)");
    }

    /// <summary>1 点分のマークと点名・標高の文字。</summary>
    public static List<Entity> Build(CadDocument doc, PointEntity p, PointMarkSettings s, Layer markLayer, Layer nameLayer)
    {
        var c = p.Position;
        var r = doc.PaperMmToMeters(s.SizeMm) / 2;
        var list = new List<Entity>();
        void Line(Vec2 a, Vec2 b) => list.Add(new LineEntity(markLayer, c + a, c + b));
        void Circle(double radius) => list.Add(new CircleEntity(markLayer, c, radius));
        void Dot(double radius) => list.Add(new HatchEntity(markLayer,
            [Enumerable.Range(0, 24).Select(i => c + Vec2.FromAngle(i * Math.PI / 12) * radius)], [], solid: true));
        var cross = r * 1.5; // 十字は〇から少しはみ出す
        switch (s.Kind)
        {
            case PointMarkKind.CrossCircle:
                Circle(r);
                Line(new(-cross, 0), new(cross, 0));
                Line(new(0, -cross), new(0, cross));
                break;
            case PointMarkKind.DotCircle:
                Circle(r);
                Dot(r * 0.3);
                break;
            case PointMarkKind.Cross:
                Line(new(-r, 0), new(r, 0));
                Line(new(0, -r), new(0, r));
                break;
            case PointMarkKind.XMark:
                var d = r * Math.Sqrt(0.5);
                Line(new(-d, -d), new(d, d));
                Line(new(-d, d), new(d, -d));
                break;
            case PointMarkKind.Circle:
                Circle(r);
                break;
            case PointMarkKind.DoubleCircle:
                Circle(r);
                Circle(r * 0.55);
                break;
            case PointMarkKind.TrianglePoint:
                // 点を重心にした正三角形
                var top = new Vec2(0, r);
                var left = top.Rotate(2 * Math.PI / 3);
                var right = top.Rotate(-2 * Math.PI / 3);
                list.Add(new PolylineEntity(markLayer, [c + top, c + left, c + right]) { IsClosed = true });
                Dot(r * 0.2);
                break;
            case PointMarkKind.Dot:
                Dot(r * 0.4);
                break;
        }

        // 点名はマークの右上、標高はその下
        var h = doc.PaperMmToMeters(s.TextHeightMm);
        var x = (s.Kind == PointMarkKind.CrossCircle ? cross : r) + doc.PaperMmToMeters(0.8);
        if (s.DrawName)
            list.Add(new TextEntity(nameLayer, c + new Vec2(x, h * 0.2), p.Name, h));
        if (s.DrawElevation && p.Z is double z)
            list.Add(new TextEntity(nameLayer, c + new Vec2(x, s.DrawName ? -h * 1.2 : h * 0.2),
                "H=" + z.ToString("F3", CultureInfo.InvariantCulture), h));

        var tag = TagOf(p);
        foreach (var e in list) e.Tag = tag;
        return list;
    }
}
