using CivilCad.Core.Geometry;

namespace CivilCad.Alignment;

/// <summary>IP 法の入力。Point は CAD 座標。Radius = 0 は曲線を入れない折れ点。A1/A2 = 0 はクロソイドなし。</summary>
public sealed record IntersectionPoint(string Name, Vec2 Point, double Radius = 0, double A1 = 0, double A2 = 0);

/// <summary>IP ごとの曲線諸元。</summary>
public sealed record CurveResult(
    string Name,
    double IA,        // 交角 (符号付き, 左 +) [rad]
    double Radius,
    double A1, double A2,
    double L1, double L2, // クロソイド長
    double Lc,            // 円曲線長
    double T1, double T2, // 接線長 (IP→始点側, IP→終点側)
    int Turn)
{
    public double CurveLength => L1 + Lc + L2;
}

public sealed record MainPoint(string Name, double Station, Vec2 Point, double Direction);

public sealed class AlignmentException(string message) : Exception(message);

/// <summary>平面線形。IP 法で直線・円曲線・クロソイドを組み立てる。</summary>
public sealed class HorizontalAlignment
{
    private const double Eps = 1e-9;

    private HorizontalAlignment(List<HorizontalElement> elements, List<CurveResult> curves, List<MainPoint> mainPoints, double startStation)
    {
        Elements = elements;
        Curves = curves;
        MainPoints = mainPoints;
        StartStation = startStation;
    }

    public IReadOnlyList<HorizontalElement> Elements { get; }
    public IReadOnlyList<CurveResult> Curves { get; }
    public IReadOnlyList<MainPoint> MainPoints { get; }
    public double StartStation { get; }
    public double Length => Elements.Sum(e => e.Length);
    public double EndStation => StartStation + Length;

    public static HorizontalAlignment FromIps(IReadOnlyList<IntersectionPoint> ips, double startStation = 0)
    {
        if (ips.Count < 2) throw new AlignmentException("BP と EP の 2 点以上が必要です。");

        var n = ips.Count;
        var dirs = new double[n - 1];
        var legs = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            var d = ips[i + 1].Point - ips[i].Point;
            legs[i] = d.Length;
            if (legs[i] < Eps) throw new AlignmentException($"{ips[i].Name} と {ips[i + 1].Name} が同じ位置です。");
            dirs[i] = d.Angle;
        }

        // 各 IP の曲線諸元
        var curves = new List<CurveResult>();
        var tanIn = new double[n];  // IP から手前側の接線長
        var tanOut = new double[n]; // IP から先側の接線長
        for (var i = 1; i < n - 1; i++)
        {
            var ip = ips[i];
            var ia = GeoMath.NormalizeSignedAngle(dirs[i] - dirs[i - 1]);
            var abs = Math.Abs(ia);
            var turn = ia >= 0 ? 1 : -1;
            if (abs < 1e-12 || ip.Radius <= 0)
            {
                curves.Add(new CurveResult(ip.Name, ia, 0, 0, 0, 0, 0, 0, 0, 0, turn));
                continue;
            }
            if (abs > Math.PI - 1e-6) throw new AlignmentException($"{ip.Name}: 交角が 180° 近くあり曲線を設置できません。");

            var r = ip.Radius;
            double l1 = 0, tau1 = 0, p1 = 0, m1 = 0, l2 = 0, tau2 = 0, p2 = 0, m2 = 0;
            if (ip.A1 > 0) (l1, tau1, _, _, p1, m1) = Clothoid.Elements(ip.A1, r);
            if (ip.A2 > 0) (l2, tau2, _, _, p2, m2) = Clothoid.Elements(ip.A2, r);

            var theta = abs - tau1 - tau2;
            if (theta < -Eps)
                throw new AlignmentException($"{ip.Name}: クロソイドが長すぎて円曲線が入りません (A を小さくするか R を大きくしてください)。");
            theta = Math.Max(theta, 0);

            var sin = Math.Sin(abs);
            var cos = Math.Cos(abs);
            var t1 = (r + p2 - (r + p1) * cos) / sin + m1;
            var t2 = (r + p1 - (r + p2) * cos) / sin + m2;
            tanIn[i] = t1;
            tanOut[i] = t2;
            curves.Add(new CurveResult(ip.Name, ia, r, ip.A1, ip.A2, l1, l2, r * theta, t1, t2, turn));
        }

        for (var i = 0; i < n - 1; i++)
        {
            var straight = legs[i] - tanOut[i] - tanIn[i + 1];
            if (straight < -1e-3) // ファイルから読んだ座標の丸め誤差 (曲線どうしが直接つながる所) は許す
                throw new AlignmentException(
                    $"{ips[i].Name}〜{ips[i + 1].Name} 間で曲線が重なっています (不足 {-straight:F3} m)。R または A を小さくしてください。");
        }

        // 要素の組み立て
        var elements = new List<HorizontalElement>();
        var mainPoints = new List<MainPoint>();
        var station = startStation;
        var cur = ips[0].Point;

        void AddElement(HorizontalElement e)
        {
            if (e.Length < Eps) return;
            e.StartStation = station;
            elements.Add(e);
            station += e.Length;
        }

        void AddMainPoint(string name, Vec2 p, double dir) => mainPoints.Add(new MainPoint(name, station, p, dir));

        AddMainPoint(ips[0].Name, cur, dirs[0]);
        for (var i = 1; i < n - 1; i++)
        {
            var c = curves[i - 1];
            if (c.Radius <= 0)
            {
                // 折れ点: IP まで直線を引き、そこから次の向きの直線
                AddElement(new LineElement(cur, dirs[i - 1], cur.DistanceTo(ips[i].Point)));
                cur = ips[i].Point;
                if (Math.Abs(c.IA) >= 1e-12) AddMainPoint(ips[i].Name, cur, dirs[i]);
                continue;
            }

            var no = i.ToString();
            var dirIn = dirs[i - 1];
            var dirOut = dirs[i];
            var start = ips[i].Point - Vec2.FromAngle(dirIn) * c.T1;
            var end = ips[i].Point + Vec2.FromAngle(dirOut) * c.T2;

            AddElement(new LineElement(cur, dirIn, cur.DistanceTo(start)));

            var arcStart = start;
            var arcDir = dirIn;
            if (c.L1 > 0)
            {
                AddMainPoint($"KA{no}-1", start, dirIn);
                var cl = new ClothoidElement(c.A1, c.L1, c.Turn, true, start, dirIn);
                AddElement(cl);
                (arcStart, arcDir) = cl.Evaluate(cl.Length);
                AddMainPoint($"KE{no}-1", arcStart, arcDir);
            }
            else
            {
                AddMainPoint($"BC{no}", start, dirIn);
            }

            AddElement(new ArcElement(arcStart, arcDir, c.Lc, c.Radius, c.Turn));

            if (c.L2 > 0)
            {
                var cl = new ClothoidElement(c.A2, c.L2, c.Turn, false, end, dirOut);
                AddMainPoint($"KE{no}-2", cl.Evaluate(0).Point, cl.Evaluate(0).Direction);
                AddElement(cl);
                AddMainPoint($"KA{no}-2", end, dirOut);
            }
            else
            {
                AddMainPoint($"EC{no}", end, dirOut);
            }
            cur = end;
        }
        AddElement(new LineElement(cur, dirs[^1], cur.DistanceTo(ips[^1].Point)));
        AddMainPoint(ips[^1].Name, ips[^1].Point, dirs[^1]);

        return new HorizontalAlignment(elements, curves, mainPoints, startStation);
    }

    /// <summary>測点 (追加距離) の中心線上の点と方向。範囲外は端に丸める。</summary>
    public (Vec2 Point, double Direction) Evaluate(double station)
    {
        station = Math.Clamp(station, StartStation, EndStation);
        var e = FindElement(station);
        return e.Evaluate(station - e.StartStation);
    }

    /// <summary>測点と幅 (右側を正) から座標を求める。</summary>
    public Vec2 PointAt(double station, double offsetRight = 0)
    {
        var (p, dir) = Evaluate(station);
        return p - Vec2.FromAngle(dir).LeftNormal * offsetRight;
    }

    /// <summary>任意点から中心線への垂線の足の測点と幅 (右側を正)。</summary>
    public (double Station, double OffsetRight) Project(Vec2 point)
    {
        // 粗く探索 → 黄金分割で詰める
        var step = Math.Max(Length / 4000, 0.1);
        var best = StartStation;
        var bestD = double.PositiveInfinity;
        for (var s = StartStation; s <= EndStation + step / 2; s += step)
        {
            var d = Evaluate(s).Point.DistanceTo(point);
            if (d < bestD) (best, bestD) = (s, d);
        }

        var lo = Math.Max(StartStation, best - step);
        var hi = Math.Min(EndStation, best + step);
        var g = (Math.Sqrt(5) - 1) / 2;
        for (var k = 0; k < 60 && hi - lo > 1e-7; k++)
        {
            var a = hi - g * (hi - lo);
            var b = lo + g * (hi - lo);
            if (Evaluate(a).Point.DistanceTo(point) < Evaluate(b).Point.DistanceTo(point)) hi = b;
            else lo = a;
        }
        var st = (lo + hi) / 2;
        var (p, dir) = Evaluate(st);
        var offsetLeft = Vec2.FromAngle(dir).Cross(point - p);
        return (st, -offsetLeft);
    }

    /// <summary>中心線を折れ線化する (要素の境界は必ず含む)。</summary>
    public IEnumerable<(double Station, Vec2 Point)> Sample(double maxStep)
    {
        foreach (var e in Elements)
        {
            var count = e.Kind == ElementKind.Line ? 1 : Math.Max(4, (int)Math.Ceiling(e.Length / maxStep));
            for (var k = 0; k < count; k++)
            {
                var s = e.Length * k / count;
                yield return (e.StartStation + s, e.Evaluate(s).Point);
            }
        }
        yield return (EndStation, Evaluate(EndStation).Point);
    }

    private HorizontalElement FindElement(double station)
    {
        int lo = 0, hi = Elements.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Elements[mid].StartStation <= station) lo = mid;
            else hi = mid - 1;
        }
        return Elements[lo];
    }
}
