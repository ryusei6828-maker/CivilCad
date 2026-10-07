using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

public enum SnapKind { Endpoint, Midpoint, Center, Quadrant, Node, Intersection, Perpendicular, Tangent, Nearest }

public readonly record struct SnapPoint(Vec2 Point, SnapKind Kind);

/// <summary>図形の基底クラス。座標は CAD 座標 (x=東, y=北, m)。</summary>
public abstract class Entity(Layer layer)
{
    public Layer Layer { get; set; } = layer;

    /// <summary>図形固有の色。null ならレイヤ色 (ByLayer)。</summary>
    public RgbColor? Color { get; set; }

    /// <summary>生成元の識別用 (例: 路線作図で生成した図形は "ALIGN:路線名")。</summary>
    public string? Tag { get; set; }

    /// <summary>線幅 (印刷時の mm)。null なら既定 (細線)。</summary>
    public double? LineWeight { get; set; }

    public RgbColor EffectiveColor => Color ?? Layer.Color;

    public abstract Box2 Bounds { get; }
    public abstract double DistanceTo(Vec2 p);
    public abstract void Translate(Vec2 delta);

    /// <summary>origin を中心に rad (反時計回り) だけ回転する。</summary>
    public abstract void Rotate(Vec2 origin, double rad);

    /// <summary>origin を中心に k 倍に拡大縮小する (k &gt; 0)。</summary>
    public abstract void Scale(Vec2 origin, double k);

    protected static Vec2 ScalePoint(Vec2 p, Vec2 origin, double k) => origin + (p - origin) * k;
    public abstract Entity Clone();
    public virtual IEnumerable<SnapPoint> SnapPoints => [];

    /// <summary>交点・垂線・接点・近接点スナップ用の形状 (線分・円・円弧)。文字などは空。</summary>
    public virtual IEnumerable<Curve> Curves => [];

    /// <summary>交差選択用。既定はバウンディングボックスで判定。</summary>
    public virtual bool IntersectsBox(Box2 box) => Bounds.Intersects(box);

    protected T CopyPropertiesTo<T>(T e) where T : Entity
    {
        e.Color = Color;
        e.Tag = Tag;
        e.LineWeight = LineWeight;
        return e;
    }
}
