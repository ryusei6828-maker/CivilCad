using System.Globalization;
using System.IO;
using System.Text;
using CivilCad.Core.Commands;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>座標管理の一覧の 1 行 (測量座標 X = 北, Y = 東)。</summary>
public sealed record CoordinateRow(PointEntity Point, string Name, double X, double Y, double? Z, string Tags = "");

/// <summary>
/// 登録された座標 (点名のある点) の一覧と、CSV / SIMA への書き出し。
/// 座標は座標設定の向きを含めた測量座標 (X = 北, Y = 東)。
/// </summary>
/// <summary>座標の並べ替えの基準。</summary>
public enum CoordinateSort { NameAsc, NameDesc, XAsc, XDesc, YAsc, YDesc, ZAsc, ZDesc, TagsAsc }

public static class CoordinateRegistry
{
    /// <summary>登録された座標 (点名のある点) を図面の並び順で。</summary>
    public static List<CoordinateRow> Rows(CadDocument doc) =>
    [
        .. doc.Entities.OfType<PointEntity>().Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p =>
        {
            var (x, y) = doc.ToSurvey(p.Position);
            return new CoordinateRow(p, p.Name, x, y, p.Z, p.Tags);
        }),
    ];

    /// <summary>点 (PO)・座標計測 (ID) で登録するときに付けるタグ (座標管理の「登録するタグ」)。</summary>
    public static string DefaultTag { get; set; } = "";

    /// <summary>一覧に出ているタグ (重複なし、名前順)。</summary>
    public static List<string> AllTags(CadDocument doc) =>
        [.. doc.Entities.OfType<PointEntity>().Where(p => !string.IsNullOrWhiteSpace(p.Name)).SelectMany(p => p.TagList).Distinct().Order(StringComparer.CurrentCulture)];

    /// <summary>まだ使っていない点名 (prefix + 番号)。</summary>
    public static string NextName(CadDocument doc, string prefix = "P")
    {
        var used = doc.Entities.OfType<PointEntity>().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
            if (!used.Contains(prefix + i)) return prefix + i;
    }

    private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>CSV (点名,X,Y,Z,タグ。UTF-8 BOM 付きで Excel でも開ける。読み込むとタグも戻る)。</summary>
    public static void ExportCsv(IEnumerable<CoordinateRow> rows, string path)
    {
        var sb = new StringBuilder("点名,X,Y,Z,タグ\r\n");
        foreach (var r in rows)
            sb.Append(Csv(r.Name)).Append(',').Append(F(r.X)).Append(',').Append(F(r.Y)).Append(',')
              .Append(r.Z is double z ? F(z) : "").Append(',').Append(Csv(string.Join(" ", r.Point.TagList))).Append("\r\n"); // 複数のタグは空白で区切る (カンマだと列が分かれる)
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Csv(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>SIMA (A01,点番号,点名,X,Y,標高。Shift_JIS)。点番号は 1 から順に振る。SIMA にはタグの欄が無いのでタグは書かない。</summary>
    public static void ExportSima(IEnumerable<CoordinateRow> rows, string path)
    {
        var sb = new StringBuilder("G00,01,CivilCad,\r\nZ00,座標データ,\r\nA00,\r\n");
        var n = 1;
        foreach (var r in rows)
            sb.Append("A01,").Append(n++).Append(',').Append(r.Name.Replace(",", "，")).Append(',')
              .Append(F(r.X)).Append(',').Append(F(r.Y)).Append(',').Append(r.Z is double z ? F(z) : "").Append(",\r\n");
        sb.Append("A99,\r\n");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(path, sb.ToString(), Encoding.GetEncoding(932));
    }

    public static string SortLabel(CoordinateSort sort) => sort switch
    {
        CoordinateSort.NameAsc => "点名 A→Z (P2 → P10 の順)",
        CoordinateSort.NameDesc => "点名 Z→A",
        CoordinateSort.XAsc => "X (北) 小さい順",
        CoordinateSort.XDesc => "X (北) 大きい順",
        CoordinateSort.YAsc => "Y (東) 小さい順",
        CoordinateSort.YDesc => "Y (東) 大きい順",
        CoordinateSort.ZAsc => "Z (標高) 小さい順",
        CoordinateSort.ZDesc => "Z (標高) 大きい順",
        CoordinateSort.TagsAsc => "タグ順",
        _ => sort.ToString(),
    };

    /// <summary>
    /// 登録した座標を並べ替えるコマンド (元に戻せる)。座標の点どうしの順番だけを入れ替え、ほかの図形の順番は変えない。
    /// 並べ替えた順は一覧・CSV / SIMA の書き出しの順になる。同じ値どうしは今の順を保つ。点が 2 つ未満なら null。
    /// </summary>
    public static IUndoableCommand? SortCommand(CadDocument doc, CoordinateSort sort)
    {
        var rows = Rows(doc);
        if (rows.Count < 2) return null;
        IEnumerable<CoordinateRow> sorted = sort switch
        {
            CoordinateSort.NameAsc => rows.OrderBy(r => r.Name, NaturalComparer.Instance),
            CoordinateSort.NameDesc => rows.OrderByDescending(r => r.Name, NaturalComparer.Instance),
            CoordinateSort.XAsc => rows.OrderBy(r => r.X),
            CoordinateSort.XDesc => rows.OrderByDescending(r => r.X),
            CoordinateSort.YAsc => rows.OrderBy(r => r.Y),
            CoordinateSort.YDesc => rows.OrderByDescending(r => r.Y),
            // 標高の無い点は最後
            CoordinateSort.ZAsc => rows.OrderBy(r => r.Z is null).ThenBy(r => r.Z),
            CoordinateSort.ZDesc => rows.OrderBy(r => r.Z is null).ThenByDescending(r => r.Z),
            CoordinateSort.TagsAsc => rows.OrderBy(r => r.Tags.Length == 0).ThenBy(r => r.Tags, NaturalComparer.Instance).ThenBy(r => r.Name, NaturalComparer.Instance),
            _ => rows,
        };
        var queue = new Queue<PointEntity>(sorted.Select(r => r.Point));
        var points = rows.Select(r => r.Point).ToHashSet();
        var order = doc.Entities.Select(e => e is PointEntity p && points.Contains(p) ? queue.Dequeue() : e).ToList();
        return order.SequenceEqual(doc.Entities) ? null : new ReorderEntitiesCommand(order, $"座標の並べ替え ({SortLabel(sort)})");
    }

    /// <summary>数字の部分を数として比べる (P2 &lt; P10、No.9 &lt; No.10)。大文字・小文字は区別しない。</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static NaturalComparer Instance { get; } = new();

        public int Compare(string? a, string? b)
        {
            a ??= "";
            b ??= "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
                {
                    var si = i;
                    var sj = j;
                    while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                    while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                    var na = a[si..i].TrimStart('0');
                    var nb = b[sj..j].TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    var c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                    continue;
                }
                var d = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.OrdinalIgnoreCase);
                if (d != 0) return d;
                i++;
                j++;
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
