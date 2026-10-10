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

    /// <summary>標高 (m)。無ければ null。</summary>
    public double? Z { get; set; }

    public override Box2 Bounds => new(Position, Position);
    public override double DistanceTo(Vec2 p) => p.DistanceTo(Position);
    public override bool IntersectsBox(Box2 box) => box.Contains(Position);
    public override void Translate(Vec2 delta) => Position += delta;
    public override void Rotate(Vec2 origin, double rad) => Position = Position.RotateAround(origin, rad);
    public override void Scale(Vec2 origin, double k) => Position = ScalePoint(Position, origin, k);
    public override Entity Clone() => CopyPropertiesTo(new PointEntity(Layer, Position) { Name = Name, Z = Z });
    public override IEnumerable<SnapPoint> SnapPoints => [new(Position, SnapKind.Node)];
}
