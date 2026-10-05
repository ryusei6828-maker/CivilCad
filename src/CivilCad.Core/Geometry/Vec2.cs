namespace CivilCad.Core.Geometry;

/// <summary>
/// 2次元ベクトル/座標。CAD内部座標は x = 東, y = 北 (メートル単位)。
/// 測量座標 (X = 北, Y = 東) との変換は <see cref="FromSurvey"/> / <see cref="ToSurvey"/> を使う。
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);
    public static Vec2 operator *(double k, Vec2 a) => new(a.X * k, a.Y * k);
    public static Vec2 operator /(Vec2 a, double k) => new(a.X / k, a.Y / k);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;
    public double Angle => Math.Atan2(Y, X);

    /// <summary>進行方向に対して左向きの法線 (90°反時計回り)。</summary>
    public Vec2 LeftNormal => new(-Y, X);

    public Vec2 Normalized()
    {
        var l = Length;
        return l > 0 ? this / l : Zero;
    }

    public double Dot(Vec2 o) => X * o.X + Y * o.Y;
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;
    public double DistanceTo(Vec2 o) => (this - o).Length;

    public Vec2 Rotate(double rad)
    {
        var c = Math.Cos(rad);
        var s = Math.Sin(rad);
        return new(X * c - Y * s, X * s + Y * c);
    }

    /// <summary>origin を中心に rad だけ回転した点。</summary>
    public Vec2 RotateAround(Vec2 origin, double rad) => origin + (this - origin).Rotate(rad);

    public static Vec2 FromAngle(double rad) => new(Math.Cos(rad), Math.Sin(rad));

    /// <summary>測量座標 (X=北, Y=東) から CAD 座標へ。</summary>
    public static Vec2 FromSurvey(double surveyX, double surveyY) => new(surveyY, surveyX);

    /// <summary>CAD 座標から測量座標 (X=北, Y=東) へ。</summary>
    public (double X, double Y) ToSurvey() => (Y, X);

    public override string ToString() => $"({X:F3}, {Y:F3})";
}
