using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using CivilCad.Alignment;

namespace CivilCad.App.Alignment;

/// <summary>
/// 路線パネルの表の 1 行。数値も文字のまま持つ (空欄や貼り付けの途中の値を受け付けるため)。
/// 座標は測量座標 (X = 北, Y = 東)。
/// </summary>
public sealed class RouteRow : INotifyPropertyChanged
{
    private string _kind = RouteTable.KindIp, _name = "", _x = "", _y = "", _r = "", _a1 = "", _a2 = "";

    /// <summary>属性: 始点 / IP / クロソイド / 終点。</summary>
    public string Kind { get => _kind; set => Set(ref _kind, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string X { get => _x; set => Set(ref _x, value); }
    public string Y { get => _y; set => Set(ref _y, value); }
    public string R { get => _r; set => Set(ref _r, value); }
    public string A1 { get => _a1; set => Set(ref _a1, value); }
    public string A2 { get => _a2; set => Set(ref _a2, value); }

    public bool IsEmpty => _name.Trim().Length == 0 && _x.Trim().Length == 0 && _y.Trim().Length == 0 && _r.Trim().Length == 0;

    /// <summary>列の番号で値を読み書きする (貼り付け用。0 = 属性, 1 = 点名, 2 = X, 3 = Y, 4 = R, 5 = A1, 6 = A2)。</summary>
    public string this[int column]
    {
        get => column switch { 0 => Kind, 1 => Name, 2 => X, 3 => Y, 4 => R, 5 => A1, 6 => A2, _ => "" };
        set
        {
            switch (column)
            {
                case 0: Kind = RouteTable.NormalizeKind(value) ?? Kind; break;
                case 1: Name = value; break;
                case 2: X = value; break;
                case 3: Y = value; break;
                case 4: R = value; break;
                case 5: A1 = value; break;
                case 6: A2 = value; break;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        value ??= "";
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}

/// <summary>路線パネルの表の読み書き (属性の判別・Excel からの貼り付け・IP 表への変換)。</summary>
public static class RouteTable
{
    public const string KindStart = "始点";
    public const string KindIp = "IP";
    public const string KindClothoid = "クロソイド";
    public const string KindEnd = "終点";

    public static IReadOnlyList<string> Kinds { get; } = [KindStart, KindIp, KindClothoid, KindEnd];

    public const int ColumnCount = 7;

    /// <summary>属性の書き方の揺れ (BP・起点・EP・CL など) をそろえる。分からなければ null。</summary>
    public static string? NormalizeKind(string? text)
    {
        var t = (text ?? "").Trim().ToUpperInvariant();
        return t switch
        {
            "始点" or "起点" or "BP" or "START" => KindStart,
            "終点" or "EP" or "END" => KindEnd,
            "IP" or "交点" or "単曲線" or "円曲線" or "折点" => KindIp,
            "クロソイド" or "CL" or "CLOTHOID" or "緩和曲線" => KindClothoid,
            _ => null,
        };
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s.Trim().Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>
    /// Excel などからコピーした表 (タブ区切り、行は改行) を、row 行目・column 列目から右下へ貼る (Excel と同じ)。
    /// 行が足りなければ増やす。1 行目が見出し (X・Y など数値の列に数でない文字) なら飛ばす。戻り値は貼った行数。
    /// </summary>
    public static int Paste(IList<RouteRow> rows, int row, int column, string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        var cells = lines.Select(l => l.Split('\t')).ToList();
        if (cells.Count == 0) return 0;
        // 見出し行: 数値の列 (X・Y・R・A) に数でない文字がある
        bool IsHeader(string[] c) => c.Select((v, i) => (v, col: column + i))
            .Any(x => x.col is >= 2 and <= 6 && x.v.Trim().Length > 0 && !TryNum(x.v, out _));
        if (IsHeader(cells[0])) cells.RemoveAt(0);
        row = Math.Max(0, row);
        column = Math.Clamp(column, 0, ColumnCount - 1);
        for (var i = 0; i < cells.Count; i++)
        {
            while (rows.Count <= row + i) rows.Add(new RouteRow());
            var target = rows[row + i];
            for (var j = 0; j < cells[i].Length && column + j < ColumnCount; j++)
                target[column + j] = cells[i][j].Trim();
        }
        FixKinds(rows);
        return cells.Count;
    }

    /// <summary>空でない行のうち、最初を始点・最後を終点に、途中の始点・終点を IP にする。点名が空なら BP・IP1…・EP を付ける。</summary>
    public static void FixKinds(IList<RouteRow> rows)
    {
        var used = rows.Where(r => !r.IsEmpty).ToList();
        for (var i = 0; i < used.Count; i++)
        {
            var r = used[i];
            if (i == 0) r.Kind = KindStart;
            else if (i == used.Count - 1 && used.Count >= 2) r.Kind = KindEnd;
            else if (r.Kind is KindStart or KindEnd) r.Kind = KindIp;
            if (r.Name.Trim().Length == 0)
                r.Name = r.Kind switch { KindStart => "BP", KindEnd => "EP", _ => $"IP{i}" };
        }
    }

    /// <summary>
    /// 表 → IP 表 (路線計算の入力)。空の行は飛ばす。最初の行を始点、最後の行を終点として計算する。
    /// IP は R (0 なら折点)、クロソイドは R と A1 (A2 が空なら A1 と同じ)。正しくなければ AlignmentException (何行目か付き)。
    /// </summary>
    public static AlignmentInput ToInput(IEnumerable<RouteRow> rows, AlignmentInput settings)
    {
        var input = settings.Clone();
        input.Rows.Clear();
        var used = rows.Where(r => !r.IsEmpty).ToList();
        if (used.Count < 2) throw new AlignmentException("始点と終点の 2 行以上を入れてください。");
        for (var i = 0; i < used.Count; i++)
        {
            var r = used[i];
            var no = $"{i + 1} 行目 ({(r.Name.Trim().Length > 0 ? r.Name.Trim() : r.Kind)})";
            if (!TryNum(r.X, out var x) || !TryNum(r.Y, out var y)) throw new AlignmentException($"{no}: 座標 X・Y を数値で入れてください。");
            double rad = 0, a1 = 0, a2 = 0;
            var middle = i > 0 && i < used.Count - 1;
            if (middle)
            {
                if (r.R.Trim().Length > 0 && (!TryNum(r.R, out rad) || rad < 0)) throw new AlignmentException($"{no}: R は 0 以上の数値にしてください (0 は曲線なしの折点)。");
                if (r.Kind == KindClothoid)
                {
                    if (!(rad > 0)) throw new AlignmentException($"{no}: クロソイドには R (円曲線の半径) を入れてください。");
                    if (!TryNum(r.A1, out a1) || !(a1 > 0)) throw new AlignmentException($"{no}: クロソイドには A1 (クロソイドのパラメータ) を入れてください。");
                    a2 = r.A2.Trim().Length == 0 ? a1 : TryNum(r.A2, out var v) && v > 0 ? v : throw new AlignmentException($"{no}: A2 は 0 より大きい数値にしてください (空なら A1 と同じ)。");
                }
            }
            var name = r.Name.Trim().Length > 0 ? r.Name.Trim() : i == 0 ? "BP" : i == used.Count - 1 ? "EP" : $"IP{i}";
            input.Rows.Add(new IpRow { Name = name, X = x, Y = y, R = rad, A1 = a1, A2 = a2 });
        }
        return input;
    }

    /// <summary>IP 表 → 表の行 (路線計算・XML から読んだ路線を開くとき)。</summary>
    public static List<RouteRow> FromInput(AlignmentInput input)
    {
        string N(double v) => v == 0 ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
        var list = input.Rows.Select((r, i) => new RouteRow
        {
            Kind = i == 0 ? KindStart : i == input.Rows.Count - 1 ? KindEnd : r.A1 > 0 || r.A2 > 0 ? KindClothoid : KindIp,
            Name = r.Name,
            X = r.X.ToString("0.000", CultureInfo.InvariantCulture),
            Y = r.Y.ToString("0.000", CultureInfo.InvariantCulture),
            R = N(r.R),
            A1 = N(r.A1),
            A2 = N(r.A2),
        }).ToList();
        return list;
    }
}
