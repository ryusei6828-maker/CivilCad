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

/// <summary>路線計算ダイアログの入力内容 (再表示できるよう保持する)。</summary>
public sealed class AlignmentInput
{
    public string Name { get; set; } = "路線1";
    public double StartStation { get; set; }
    public double Pitch { get; set; } = Station.DefaultPitch;
    /// <summary>注記の文字高 (用紙上の mm)。図面の縮尺で m に換算する。</summary>
    public double TextHeightMm { get; set; } = 3.5;
    public ObservableCollection<IpRow> Rows { get; set; } = [];

    public string Tag => "ALIGN:" + Name;

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
        Rows = [.. Rows.Select(r => new IpRow { Name = r.Name, X = r.X, Y = r.Y, R = r.R, A1 = r.A1, A2 = r.A2 })],
    };
}
