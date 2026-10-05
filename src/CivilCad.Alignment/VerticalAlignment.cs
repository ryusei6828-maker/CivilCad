namespace CivilCad.Alignment;

/// <summary>縦断変化点。CurveLength は縦断曲線長 (0 なら曲線なし)。</summary>
public sealed record VerticalIp(double Station, double Elevation, double CurveLength = 0);

/// <summary>縦断線形 (勾配直線 + 2次放物線の縦断曲線)。</summary>
public sealed class VerticalAlignment
{
    private readonly VerticalIp[] _ips;

    public VerticalAlignment(IEnumerable<VerticalIp> ips)
    {
        _ips = ips.OrderBy(p => p.Station).ToArray();
        if (_ips.Length < 2) throw new AlignmentException("縦断変化点は 2 点以上必要です。");
        for (var i = 0; i + 1 < _ips.Length; i++)
        {
            if (_ips[i + 1].Station - _ips[i].Station <= 0)
                throw new AlignmentException("縦断変化点の測点が重複しています。");
            var used = HalfCurve(i) + HalfCurve(i + 1);
            if (used > _ips[i + 1].Station - _ips[i].Station + 1e-9)
                throw new AlignmentException($"追加距離 {_ips[i].Station:F3}〜{_ips[i + 1].Station:F3} 間で縦断曲線が重なっています。");
        }
    }

    public IReadOnlyList<VerticalIp> Ips => _ips;

    /// <summary>区間 i (変化点 i → i+1) の勾配 (小数。1% = 0.01)。</summary>
    public double Grade(int i) =>
        (_ips[i + 1].Elevation - _ips[i].Elevation) / (_ips[i + 1].Station - _ips[i].Station);

    /// <summary>計画高。</summary>
    public double ElevationAt(double station)
    {
        // 縦断曲線区間内なら放物線
        for (var i = 1; i < _ips.Length - 1; i++)
        {
            var l = _ips[i].CurveLength;
            if (l <= 0) continue;
            var bvc = _ips[i].Station - l / 2;
            if (station < bvc || station > bvc + l) continue;
            var g1 = Grade(i - 1);
            var g2 = Grade(i);
            var x = station - bvc;
            var eBvc = _ips[i].Elevation - g1 * l / 2;
            return eBvc + g1 * x + (g2 - g1) / (2 * l) * x * x;
        }
        return GradeLineElevation(station);
    }

    private double HalfCurve(int i) => i == 0 || i == _ips.Length - 1 ? 0 : _ips[i].CurveLength / 2;

    private double GradeLineElevation(double station)
    {
        var i = 0;
        while (i < _ips.Length - 2 && station > _ips[i + 1].Station) i++;
        return _ips[i].Elevation + Grade(i) * (station - _ips[i].Station);
    }
}
