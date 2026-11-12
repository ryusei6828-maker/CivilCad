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

/// <summary>
/// 要素表の 1 行 (要素法)。種類は 直線 / 円曲線 / クロソイド。R が 0 または空は ∞ (クロソイドの直線側)。
/// 回転は 右 / 左 (直線は不要)。A はクロソイドの パラメータ (表示のみ。A² = L / |1/R2 − 1/R1|)。
/// </summary>
public sealed class ElementRow : System.ComponentModel.INotifyPropertyChanged
{
    public const string KindLine = "直線";
    public const string KindArc = "円曲線";
    public const string KindClothoid = "クロソイド";
    public const string TurnRight = "右";
    public const string TurnLeft = "左";

    public static IReadOnlyList<string> Kinds { get; } = [KindLine, KindArc, KindClothoid];
    public static IReadOnlyList<string> Turns { get; } = ["", TurnRight, TurnLeft];

    private string _kind = KindLine, _turn = "", _endName = "";
    private double _length, _r1, _r2;

    public string Kind { get => _kind; set { _kind = value ?? KindLine; Changed(nameof(Kind)); Changed(nameof(A)); } }
    public double Length { get => _length; set { _length = value; Changed(nameof(Length)); Changed(nameof(A)); } }
    /// <summary>始点の半径 (円曲線はこの半径)。0 は ∞。</summary>
    public double R1 { get => _r1; set { _r1 = value; Changed(nameof(R1)); Changed(nameof(A)); } }
    /// <summary>終点の半径 (クロソイドのみ)。0 は ∞。</summary>
    public double R2 { get => _r2; set { _r2 = value; Changed(nameof(R2)); Changed(nameof(A)); } }
    public string Turn { get => _turn; set { _turn = value ?? ""; Changed(nameof(Turn)); } }
    public string EndName { get => _endName; set { _endName = value ?? ""; Changed(nameof(EndName)); } }

    /// <summary>クロソイドパラメータ (表示のみ。クロソイド以外・半径が決まらないときは 0)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double A => ToDefOrNull()?.A ?? 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => _length == 0 && _r1 == 0 && _r2 == 0 && _endName.Trim().Length == 0;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));

    public ElementRow Clone() => new() { Kind = Kind, Length = Length, R1 = R1, R2 = R2, Turn = Turn, EndName = EndName };

    private ElementDef? ToDefOrNull()
    {
        try { return ToDef(0); }
        catch (AlignmentException) { return null; }
    }

    /// <summary>計算用の要素にする。正しくなければ AlignmentException (何番目か付き。index は 0 始まり)。</summary>
    public ElementDef ToDef(int index)
    {
        var no = $"{index + 1} 行目の要素";
        var name = _endName.Trim().Length > 0 ? _endName.Trim() : null;
        if (!(_length > 0)) throw new AlignmentException($"{no}: 延長は 0 より大きい数値にしてください。");
        var turn = _turn == TurnLeft ? 1 : _turn == TurnRight ? -1 : 0;
        static double Rad(double r) => r > 0 ? r : double.PositiveInfinity;
        switch (_kind)
        {
            case KindArc:
                if (!(_r1 > 0)) throw new AlignmentException($"{no}: 円曲線の半径 (始点 R) を入れてください。");
                if (turn == 0) throw new AlignmentException($"{no}: 回転 (右・左) を選んでください。");
                return ElementDef.Arc(_length, _r1, turn, name);
            case KindClothoid:
                if (!(_r1 > 0) && !(_r2 > 0)) throw new AlignmentException($"{no}: クロソイドの始点 R か終点 R のどちらかを入れてください (空は ∞)。");
                if (turn == 0) throw new AlignmentException($"{no}: 回転 (右・左) を選んでください。");
                return ElementDef.Spiral(_length, Rad(_r1), Rad(_r2), turn, name);
            default:
                return ElementDef.Line(_length, name);
        }
    }

    /// <summary>要素の定義から行を作る (読み込んだ路線・IP 法の線形を要素表にするとき)。</summary>
    public static ElementRow FromDef(ElementDef d)
    {
        static double R(double r) => double.IsInfinity(r) || r <= 0 ? 0 : r;
        return new ElementRow
        {
            Kind = d.Kind switch { ElementKind.Arc => KindArc, ElementKind.Clothoid => KindClothoid, _ => KindLine },
            Length = d.Length,
            R1 = d.Kind == ElementKind.Line ? 0 : R(d.RadiusStart),
            R2 = d.Kind == ElementKind.Clothoid ? R(d.RadiusEnd) : 0,
            Turn = d.Turn > 0 ? TurnLeft : d.Turn < 0 ? TurnRight : "",
            EndName = d.EndName ?? "",
        };
    }
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

    /// <summary>true なら要素法 (始点・方向角・要素表で線形を決める)。false (昔のファイルも) は IP 法。</summary>
    public bool IsElementMethod { get; set; }

    /// <summary>要素法の始点の名前・座標 (測量座標 X = 北, Y = 東)・方向角 (測量の方向角, 北 = 0 で時計回り, 度の小数)。</summary>
    public string StartName { get; set; } = "BP";
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double StartAzimuthDeg { get; set; }

    /// <summary>要素法の要素表。</summary>
    public ObservableCollection<ElementRow> ElementRows { get; set; } = [];

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
        if (IsElementMethod)
        {
            var defs = ElementRows.Where(r => !r.IsEmpty).Select((r, i) => r.ToDef(i)).ToList();
            if (defs.Count == 0) throw new AlignmentException("要素を 1 つ以上入れてください。");
            return HorizontalAlignment.FromElements(Vec2.FromSurvey(StartX, StartY), Angles.FromSurveyAzimuth(StartAzimuthDeg * Math.PI / 180),
                defs, StartStation, StartName);
        }
        var rows = Rows.Where(r => !string.IsNullOrWhiteSpace(r.Name) || r.X != 0 || r.Y != 0).ToList();
        var ips = rows.Select((r, i) => new IntersectionPoint(
            string.IsNullOrWhiteSpace(r.Name) ? $"IP{i}" : r.Name.Trim(),
            Vec2.FromSurvey(r.X, r.Y), r.R, r.A1, r.A2)).ToList();
        return HorizontalAlignment.FromIps(ips, StartStation);
    }

    /// <summary>
    /// 計算した線形 (IP 法でも要素法でも) の要素を要素表にして、要素法に切り替える (IP 表はそのまま残す)。
    /// 始点・方向角・各要素の終点名 (主要点の名前) も入れる。
    /// </summary>
    public void SetElementsFrom(HorizontalAlignment al)
    {
        var first = al.Elements[0];
        var (x, y) = first.StartPoint.ToSurvey();
        StartX = x;
        StartY = y;
        StartAzimuthDeg = Angles.ToSurveyAzimuth(first.StartDirection) * 180 / Math.PI;
        StartName = al.MainPoints.Count > 0 ? al.MainPoints[0].Name : "BP";
        ElementRows = [.. al.Elements.Select(e => ElementRow.FromDef(ElementDef.FromElement(e,
            al.MainPoints.Skip(1).FirstOrDefault(m => Math.Abs(m.Station - e.EndStation) < 1e-6)?.Name)))];
        IsElementMethod = true;
    }

    /// <summary>LandXML から読んだ路線を入力にする (要素法の路線・IP 法で表せない路線は要素表、ほかは IP 表。文字高は今の値を引き継ぐ)。</summary>
    public static AlignmentInput FromLandXml(LandXmlAlignment a, double textHeightMm)
    {
        if ((a.IsElementMethod || !a.HasIps) && a.HasElements)
        {
            var (sx, sy) = a.ElementStart.ToSurvey();
            return new AlignmentInput
            {
                Name = a.Name,
                StartStation = a.StartStation,
                Pitch = a.Pitch ?? Station.DefaultPitch,
                TextHeightMm = textHeightMm,
                IsElementMethod = true,
                StartName = a.StartName ?? "BP",
                StartX = sx,
                StartY = sy,
                StartAzimuthDeg = Angles.ToSurveyAzimuth(a.ElementStartDirection) * 180 / Math.PI,
                ElementRows = [.. a.Elements!.Select(ElementRow.FromDef)],
            };
        }
        return FromLandXmlIps(a, textHeightMm);
    }

    private static AlignmentInput FromLandXmlIps(LandXmlAlignment a, double textHeightMm) => new()
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
        IsElementMethod = IsElementMethod,
        StartName = StartName,
        StartX = StartX,
        StartY = StartY,
        StartAzimuthDeg = StartAzimuthDeg,
        ElementRows = [.. ElementRows.Select(r => r.Clone())],
    };
}
