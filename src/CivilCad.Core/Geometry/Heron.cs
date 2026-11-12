namespace CivilCad.Core.Geometry;

/// <summary>ヘロン面積の頂点 (位置・標高・点名)。標高が無ければ水平距離で辺長を出す。</summary>
public readonly record struct HeronVertex(Vec2 Position, double? Z, string Name = "");

/// <summary>ヘロンの公式で求めた三角形 1 個分の結果。辺は丸めたあとの値。</summary>
public sealed record HeronTriangle(double A, double B, double C, double S, double Area)
{
    public double SA => S - A;
    public double SB => S - B;
    public double SC => S - C;
}

/// <summary>
/// ヘロン面積 (舗装工などの実測図の面積計算書)。辺長を先に丸め、丸めた辺から s = (a+b+c)/2 と面積を求め、
/// 面積も丸める (合計は丸めた面積の和)。
/// </summary>
public static class Heron
{
    /// <summary>2 点間の辺長。slope で両方に標高があれば斜距離 (3D)、そうでなければ水平距離。</summary>
    public static double SideLength(HeronVertex a, HeronVertex b, bool slope)
    {
        var d = a.Position.DistanceTo(b.Position);
        if (slope && a.Z is double za && b.Z is double zb)
        {
            var dz = zb - za;
            return Math.Sqrt(d * d + dz * dz);
        }
        return d;
    }

    public static double Round(double v, int decimals) => Math.Round(v, decimals, MidpointRounding.AwayFromZero);

    public static HeronTriangle Compute(double a, double b, double c, int sideDecimals, int areaDecimals)
    {
        a = Round(a, sideDecimals);
        b = Round(b, sideDecimals);
        c = Round(c, sideDecimals);
        var s = (a + b + c) / 2;
        var radicand = s * (s - a) * (s - b) * (s - c);
        var area = Round(Math.Sqrt(Math.Max(0, radicand)), areaDecimals);
        return new HeronTriangle(a, b, c, s, area);
    }
}
