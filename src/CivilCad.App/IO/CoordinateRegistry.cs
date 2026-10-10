using System.Globalization;
using System.IO;
using System.Text;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>座標管理の一覧の 1 行 (測量座標 X = 北, Y = 東)。</summary>
public sealed record CoordinateRow(PointEntity Point, string Name, double X, double Y, double? Z);

/// <summary>
/// 登録された座標 (点名のある点) の一覧と、CSV / SIMA への書き出し。
/// 座標は座標設定の向きを含めた測量座標 (X = 北, Y = 東)。
/// </summary>
public static class CoordinateRegistry
{
    /// <summary>登録された座標 (点名のある点) を図面の並び順で。</summary>
    public static List<CoordinateRow> Rows(CadDocument doc) =>
    [
        .. doc.Entities.OfType<PointEntity>().Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p =>
        {
            var (x, y) = doc.ToSurvey(p.Position);
            return new CoordinateRow(p, p.Name, x, y, p.Z);
        }),
    ];

    /// <summary>まだ使っていない点名 (prefix + 番号)。</summary>
    public static string NextName(CadDocument doc, string prefix = "P")
    {
        var used = doc.Entities.OfType<PointEntity>().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
            if (!used.Contains(prefix + i)) return prefix + i;
    }

    private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>CSV (点名,X,Y,Z。UTF-8 BOM 付きで Excel でも開ける)。</summary>
    public static void ExportCsv(IEnumerable<CoordinateRow> rows, string path)
    {
        var sb = new StringBuilder("点名,X,Y,Z\r\n");
        foreach (var r in rows)
            sb.Append(Csv(r.Name)).Append(',').Append(F(r.X)).Append(',').Append(F(r.Y)).Append(',')
              .Append(r.Z is double z ? F(z) : "").Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Csv(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>SIMA (A01,点番号,点名,X,Y,標高。Shift_JIS)。点番号は 1 から順に振る。</summary>
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
}
