using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

/// <summary>
/// 点。画面では大きさ一定の小さな印、印刷では小さな黒丸で表す。スナップは「点」(Node)。
/// 点名 (Name) のある点は「登録された座標」として座標管理の一覧に出る (座標読込の点・幅杭・点コマンドの点)。
/// </summary>
public sealed class PointEntity(Layer layer, Vec2 position) : Entity(layer)
{
    public Vec2 Position { get; set; } = position;

    /// <summary>点名。空なら座標管理の一覧に出さない。</summary>
    public string Name { get; set; } = "";

    /// <summary>点名を点の右上に表示するか (画面・PDF 印刷。大きさは画面で一定・用紙上 2.5mm)。座標管理で点ごと・まとめて切り替える。</summary>
    public bool ShowName { get; set; }

    /// <summary>点名の文字高 (用紙上の mm。PDF 印刷)。</summary>
    public const double NameHeightMm = 2.5;

    /// <summary>標高 (m)。無ければ null。</summary>
    public double? Z { get; set; }

    /// <summary>タグ (分類。例: 幅杭・境界・BM)。カンマ区切りで複数も可。</summary>
    public string Tags { get; set; } = "";

    /// <summary>タグを 1 つずつに分けたもの (カンマ・読点・空白で区切る)。</summary>
    public IEnumerable<string> TagList => Tags.Split([',', '、', '，', ' ', '　'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override Box2 Bounds => new(Position, Position);
    public override double DistanceTo(Vec2 p) => p.DistanceTo(Position);
    public override bool IntersectsBox(Box2 box) => box.Contains(Position);
    public override void Translate(Vec2 delta) => Position += delta;
    public override void Mirror(Vec2 a, Vec2 b) => Position = Position.ReflectAcross(a, b);
    public override void Rotate(Vec2 origin, double rad) => Position = Position.RotateAround(origin, rad);
    public override void Scale(Vec2 origin, double k) => Position = ScalePoint(Position, origin, k);
    public override Entity Clone() => CopyPropertiesTo(new PointEntity(Layer, Position) { Name = Name, Z = Z, Tags = Tags, ShowName = ShowName });
    public override IEnumerable<SnapPoint> SnapPoints => [new(Position, SnapKind.Node)];
}
