using System.Globalization;
using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

/// <summary>
/// 画像 (写真・オルソ画像など)。画像ファイルは図面に埋め込まず、ファイルの場所 (Path) を持つ。
/// 画像の左下が Origin、U = 画像の下辺 (左→右) のベクトル、V = 左辺 (下→上) のベクトル。
/// U と V が直交でなくてもよい (ワールドファイルの回転・ゆがみもそのまま表せる)。
/// 選択は枠の線で行う (画像の上に作図するとき、中をクリックしても画像が選ばれないように)。
/// </summary>
public sealed class ImageEntity(Layer layer, string path, int pixelWidth, int pixelHeight, Vec2 origin, Vec2 u, Vec2 v) : Entity(layer)
{
    public string Path { get; set; } = path;
    public int PixelWidth { get; } = pixelWidth;
    public int PixelHeight { get; } = pixelHeight;
    public Vec2 Origin { get; set; } = origin;
    public Vec2 U { get; set; } = u;
    public Vec2 V { get; set; } = v;

    /// <summary>PDF のページ (0 から)。画像では 0。</summary>
    public int Page { get; init; }

    /// <summary>透明度 (%)。0 = 不透明。</summary>
    public double TransparencyPercent { get; set; }

    public double Width => U.Length;
    public double Height => V.Length;

    /// <summary>画像の中の位置 (s, t ともに 0〜1、t は上から下) → 図面の座標。</summary>
    public Vec2 Map(double s, double t) => Origin + U * s + V * (1 - t);

    /// <summary>図面の座標 → 画像の中の位置 (s, t)。範囲外なら 0〜1 の外の値。</summary>
    public (double S, double T) Unmap(Vec2 p)
    {
        var d = p - Origin;
        var det = U.Cross(V);
        if (Math.Abs(det) < 1e-300) return (double.NaN, double.NaN);
        var a = d.Cross(V) / det;
        var b = U.Cross(d) / det;
        return (a, 1 - b);
    }

    /// <summary>四隅 (左下・右下・右上・左上)。</summary>
    public Vec2[] Corners => [Origin, Origin + U, Origin + U + V, Origin + V];

    private IEnumerable<(Vec2 A, Vec2 B)> Edges
    {
        get
        {
            var c = Corners;
            for (var i = 0; i < 4; i++) yield return (c[i], c[(i + 1) % 4]);
        }
    }

    public override Box2 Bounds => Box2.FromPoints(Corners);

    public override double DistanceTo(Vec2 p) => Edges.Min(e => GeoMath.DistanceToSegment(p, e.A, e.B));

    /// <summary>交差選択: 枠の線が範囲にかかるか、画像全体が範囲に入るとき。</summary>
    public override bool IntersectsBox(Box2 box) =>
        Edges.Any(e => GeoMath.SegmentIntersectsBox(e.A, e.B, box)) || box.Contains(Bounds);

    public override void Translate(Vec2 delta) => Origin += delta;

    public override void Rotate(Vec2 origin, double rad)
    {
        Origin = Origin.RotateAround(origin, rad);
        U = U.Rotate(rad);
        V = V.Rotate(rad);
    }

    public override void Scale(Vec2 origin, double k)
    {
        Origin = ScalePoint(Origin, origin, k);
        U *= k;
        V *= k;
    }

    public override IEnumerable<Curve> Curves => Edges.Select(e => (Curve)new SegmentCurve(e.A, e.B));

    public override Entity Clone() =>
        CopyPropertiesTo(new ImageEntity(Layer, Path, PixelWidth, PixelHeight, Origin, U, V) { TransparencyPercent = TransparencyPercent, Page = Page });

    public override IEnumerable<SnapPoint> SnapPoints => Corners.Select(c => new SnapPoint(c, SnapKind.Endpoint));

    /// <summary>ワールドファイルの 6 つの値 (A, D, B, E, C, F) から画像の位置を作る。</summary>
    public static (Vec2 Origin, Vec2 U, Vec2 V) FromWorldFile(WorldFile w, int pixelWidth, int pixelHeight)
    {
        var col = new Vec2(w.A, w.D);   // 右へ 1 画素
        var row = new Vec2(w.B, w.E);   // 下へ 1 画素
        // C, F は左上の画素の「中心」。画像の左上の角はそこから半画素ずつ戻った所
        var topLeft = new Vec2(w.C, w.F) - col / 2 - row / 2;
        return (topLeft + row * pixelHeight, col * pixelWidth, -row * pixelHeight);
    }
}

/// <summary>
/// ワールドファイル (.jgw / .tfw / .pgw など)。6 行: A (x 方向の画素の大きさ), D, B, E (y 方向, 通常は負), C, F (左上の画素の中心)。
/// x = 東, y = 北 として扱う (GIS の慣例と同じ)。
/// </summary>
public readonly record struct WorldFile(double A, double D, double B, double E, double C, double F)
{
    public static WorldFile? Parse(string text)
    {
        var v = new List<double>();
        foreach (var line in text.Split('\n'))
        {
            var s = line.Trim();
            if (s.Length == 0) continue;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return null;
            v.Add(d);
        }
        if (v.Count < 6 || (v[0] == 0 && v[2] == 0) || (v[1] == 0 && v[3] == 0)) return null;
        return new WorldFile(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    /// <summary>画像ファイルに対応するワールドファイルの候補 (foo.jpg → foo.jgw, foo.jpgw, foo.wld)。</summary>
    public static IEnumerable<string> Candidates(string imagePath)
    {
        var ext = System.IO.Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant();
        var stem = System.IO.Path.ChangeExtension(imagePath, null);
        if (ext.Length >= 2) yield return $"{stem}.{ext[0]}{ext[^1]}w";
        yield return $"{stem}.{ext}w";
        if (ext is "jpg" or "jpeg") yield return $"{stem}.jpw";
        if (ext is "tif" or "tiff") yield return $"{stem}.tfw";
        yield return $"{stem}.wld";
    }
}
