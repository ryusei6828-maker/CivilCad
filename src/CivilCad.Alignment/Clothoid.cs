using CivilCad.Core.Geometry;

namespace CivilCad.Alignment;

/// <summary>
/// クロソイド (A² = R·L) の計算。
/// 局所座標は原点 = 曲率0の点 (KA)、x軸 = 接線方向、y軸 = 曲がる側。
/// </summary>
public static class Clothoid
{
    /// <summary>接線角 τ = s² / (2A²) [rad]。</summary>
    public static double TangentAngle(double a, double s) => s * s / (2 * a * a);

    /// <summary>曲率0の点から弧長 s の点の局所座標 (x, y)。フレネル積分の級数展開。</summary>
    public static Vec2 LocalPoint(double a, double s)
    {
        if (s == 0) return Vec2.Zero;
        var tau = TangentAngle(a, s);
        var t2 = tau * tau;
        double x = 0, y = 0;
        double termX = 1;   // (-1)^n τ^(2n)   / (2n)!
        double termY = tau; // (-1)^n τ^(2n+1) / (2n+1)!
        for (var n = 0; n < 40; n++)
        {
            var dx = termX / (4 * n + 1);
            var dy = termY / (4 * n + 3);
            x += dx;
            y += dy;
            if (Math.Abs(dx) < 1e-17 && Math.Abs(dy) < 1e-17) break;
            termX *= -t2 / ((2 * n + 1) * (2 * n + 2));
            termY *= -t2 / ((2 * n + 2) * (2 * n + 3));
        }
        return new Vec2(s * x, s * y);
    }

    /// <summary>
    /// クロソイド終点 (曲率 1/R) での諸量。
    /// ΔR: 移程量, Xm: KA から移程円中心の接線上の足までの距離。
    /// </summary>
    public static (double L, double Tau, double X, double Y, double ShiftR, double Xm) Elements(double a, double r)
    {
        var l = a * a / r;
        var tau = l / (2 * r);
        var p = LocalPoint(a, l);
        var shift = p.Y - r * (1 - Math.Cos(tau));
        var xm = p.X - r * Math.Sin(tau);
        return (l, tau, p.X, p.Y, shift, xm);
    }
}
