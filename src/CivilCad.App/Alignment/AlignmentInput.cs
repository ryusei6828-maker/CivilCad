using System.Collections.ObjectModel;
using CivilCad.Alignment;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Alignment;

/// <summary>IP 表の 1 行。座標は測量座標 (X = 北, Y = 東)。</summary>
public sealed class IpRow
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double R { get; set; }
    public double A1 { get; set; }
    public double A2 { get; set; }
}

/// <summary>通過点 (中心杭・中間杭・主要点) の 1 つ。Point は測量座標から作った図面の座標 (x = 東, y = 北)。</summary>
public sealed record PassingPoint(string Name, string Kind, double Station, Vec2 Point);

/// <summary>路線計算ダイアログの入力内容 (再表示できるよう保持する)。</summary>
public sealed class AlignmentInput
{
    public string Name { get; set; } = "路線1";
    public double StartStation { get; set; }
    public double Pitch { get; set; } = Station.DefaultPitch;
    /// <summary>中間杭 (プラス杭) の間隔 (m)。0 なら作らない。例: 測点間隔 20m・中間杭 10m → No.1+10.00。</summary>
    public double SubPitch { get; set; }

    /// <summary>主要点 (BP・BC・EC・KA・KE・EP など) を作図・通過点に含める。</summary>
    public bool IncludeMainPoints { get; set; } = true;

    /// <summary>通過点を点名付きの点にして座標管理に登録する (タグ: 中心杭・中間杭・主要点)。</summary>
    public bool RegisterPoints { get; set; }

    /// <summary>注記の文字高 (用紙上の mm)。図面の縮尺で m に換算する。</summary>
    public double TextHeightMm { get; set; } = 3.5;
    public ObservableCollection<IpRow> Rows { get; set; } = [];

    /// <summary>中心線から左の幅 (m)。カンマ区切りで複数 (例: "3.0, 5.5" = 路肩 3.0m・用地 5.5m)。空なら作図しない。</summary>
    public string LeftWidths { get; set; } = "";

    /// <summary>中心線から右の幅 (m)。</summary>
    public string RightWidths { get; set; } = "";

    /// <summary>幅ごとに中心線と平行な線 (路肩線など) を作図する。</summary>
    public bool DrawShoulders { get; set; } = true;

    /// <summary>測点ごとに、いちばん外の幅の点 (幅杭) と、左右の幅杭を結ぶ線を作図する。</summary>
    public bool DrawStakes { get; set; } = true;

    /// <summary>幅の文字 ("3.0, 5.5") を数の並びにする。正しくなければ AlignmentException。</summary>
    public static List<double> ParseWidths(string text, string label)
    {
        var list = new List<double>();
        foreach (var part in text.Replace('、', ',').Replace('，', ',').Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) || !(w > 0))
                throw new AlignmentException($"{label}は 0 より大きい数をカンマ区切りで入れてください (例: 3.0, 5.5)。");
            list.Add(w);
        }
        return [.. list.Distinct().Order()];
    }

    public string Tag => "ALIGN:" + Name;

    public const string KindCenter = "中心杭";
    public const string KindSub = "中間杭";
    public const string KindMain = "主要点";

    /// <summary>
    /// 通過点: 測点間隔ごとの中心杭 (No.N)、中間杭の間隔ごとの中間杭 (No.N+10.00 など)、主要点 (選んでいれば)。
    /// 追加距離の順に並べ、同じ位置の点は 1 つにする (主要点 → 中心杭 → 中間杭 の順に残す)。
    /// </summary>
    public List<PassingPoint> PassingPoints(HorizontalAlignment al)
    {
        var list = new List<PassingPoint>();
        void Add(string name, string kind, double st)
        {
            if (st < al.StartStation - 1e-9 || st > al.EndStation + 1e-9) return;
            list.Add(new PassingPoint(name, kind, st, al.PointAt(st, 0)));
        }
        if (IncludeMainPoints)
            foreach (var m in al.MainPoints) Add(m.Name, KindMain, m.Station);
        var first = Math.Ceiling((al.StartStation - 1e-9) / Pitch) * Pitch;
        for (var st = first; st <= al.EndStation + 1e-9; st += Pitch) Add(Station.Format(st, Pitch, 0), KindCenter, st);
        if (SubPitch > 0 && SubPitch < Pitch)
        {
            var s0 = Math.Ceiling((al.StartStation - 1e-9) / SubPitch) * SubPitch;
            for (var st = s0; st <= al.EndStation + 1e-9; st += SubPitch)
                if (Math.Abs(st / Pitch - Math.Round(st / Pitch)) > 1e-9) Add(Station.Format(st, Pitch, 2), KindSub, st);
        }
        var result = new List<PassingPoint>();
        foreach (var p in list.OrderBy(p => p.Station).ThenBy(p => p.Kind == KindMain ? 0 : p.Kind == KindCenter ? 1 : 2))
            if (result.Count == 0 || p.Station - result[^1].Station > 1e-6) result.Add(p);
        return result;
    }

    public static AlignmentInput Sample() => new()
    {
        Rows =
        [
            new() { Name = "BP", X = 0, Y = 0 },
            new() { Name = "IP1", X = 200, Y = 150, R = 300, A1 = 150, A2 = 150 },
            new() { Name = "IP2", X = 250, Y = 450, R = 250, A1 = 120, A2 = 120 },
            new() { Name = "EP", X = 450, Y = 600 },
        ],
    };

    public HorizontalAlignment Compute()
    {
        var rows = Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name) || r.X != 0 || r.Y != 0).ToList();
        var ips = rows.Select((r, i) => new IntersectionPoint(
            string.IsNullOrWhiteSpace(r.Name) ? $"IP{i}" : r.Name.Trim(),
            Vec2.FromSurvey(r.X, r.Y), r.R, r.A1, r.A2)).ToList();
        return HorizontalAlignment.FromIps(ips, StartStation);
    }

    /// <summary>LandXML から読んだ路線を IP 表にする (文字高は今の値を引き継ぐ)。</summary>
    public static AlignmentInput FromLandXml(LandXmlAlignment a, double textHeightMm) => new()
    {
        Name = a.Name,
        StartStation = a.StartStation,
        Pitch = a.Pitch ?? Station.DefaultPitch,
        TextHeightMm = textHeightMm,
        Rows =
        [
            .. a.Ips.Select(ip =>
            {
                var (x, y) = ip.Point.ToSurvey();
                return new IpRow { Name = ip.Name, X = x, Y = y, R = ip.Radius, A1 = ip.A1, A2 = ip.A2 };
            }),
        ],
    };

    public AlignmentInput Clone() => new()
    {
        Name = Name,
        StartStation = StartStation,
        Pitch = Pitch,
        TextHeightMm = TextHeightMm,
        LeftWidths = LeftWidths,
        RightWidths = RightWidths,
        DrawShoulders = DrawShoulders,
        DrawStakes = DrawStakes,
        SubPitch = SubPitch,
        IncludeMainPoints = IncludeMainPoints,
        RegisterPoints = RegisterPoints,
        Rows = [.. Rows.Select(r => new IpRow { Name = r.Name, X = r.X, Y = r.Y, R = r.R, A1 = r.A1, A2 = r.A2 })],
    };
}
