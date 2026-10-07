using System.IO;
using CivilCad.App.Drawing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>DXF / DWG の座標の単位。</summary>
public enum ImportUnit
{
    /// <summary>実寸 m。</summary>
    Meter,

    /// <summary>実寸 cm。</summary>
    Centimeter,

    /// <summary>実寸 mm。</summary>
    Millimeter,

    /// <summary>用紙上の mm (図寸)。縮尺を掛けて実寸にする。</summary>
    PaperMillimeter,
}

/// <summary>図面ファイルの形式 (拡張子) ごとの読み書きの振り分け。</summary>
public static class DrawingFiles
{
    /// <summary>開くダイアログのフィルタ。</summary>
    public const string OpenFilter =
        "図面ファイル (*.dxf;*.dwg;*.jww;*.sfc;*.p21;*.p2z)|*.dxf;*.dwg;*.jww;*.sfc;*.p21;*.p2z|DXF ファイル (*.dxf)|*.dxf|DWG ファイル (*.dwg)|*.dwg|Jw_cad ファイル (*.jww)|*.jww|SXF (SFC) ファイル (*.sfc)|*.sfc|SXF (P21) ファイル (*.p21;*.p2z)|*.p21;*.p2z";

    /// <summary>保存ダイアログの形式 (拡張子とフィルタの表示名)。順番がフィルタの番号になる。</summary>
    public static IReadOnlyList<(string Ext, string Label)> SaveFormats { get; } =
    [
        (".dxf", "DXF ファイル (*.dxf)|*.dxf"),
        (".dwg", "DWG ファイル (AutoCAD 2013 形式) (*.dwg)|*.dwg"),
        (".jww", "Jw_cad ファイル (*.jww)|*.jww"),
        (".sfc", "SXF (SFC) ファイル (*.sfc)|*.sfc"),
        (".p21", "SXF (P21) ファイル (*.p21)|*.p21"),
    ];

    public static string SaveFilter => string.Join("|", SaveFormats.Select(f => f.Label));

    /// <summary>保存ダイアログで最初に選ぶフィルタの番号 (1 始まり)。開いたファイルと同じ形式、無ければ DXF。</summary>
    public static int SaveFilterIndex(string? path)
    {
        var ext = path != null ? Path.GetExtension(path) : null;
        var i = SaveFormats.ToList().FindIndex(f => f.Ext.Equals(ext, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i + 1 : 1;
    }

    /// <param name="Note">ログに添える説明 (未対応の図形など)。</param>
    /// <param name="Sheet">用紙の範囲 (図面座標 m)。分からなければ null。</param>
    /// <param name="FileScale">ファイルに書いてあった縮尺 (分母)。無ければ null。</param>
    /// <param name="FileUnit">DXF / DWG の座標の単位 ($INSUNITS)。分からなければ null。</param>
    /// <param name="PaperFormat">SFC / P21 / JWW: 用紙上の mm とファイルの縮尺から実寸 (m) に換算して読んだ。</param>
    public sealed record OpenResult(string Note, Box2? Sheet, double? FileScale = null, ImportUnit? FileUnit = null, bool PaperFormat = false);

    /// <summary>ファイルを読み込んで doc を置き換える。</summary>
    public static OpenResult Open(CadDocument doc, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".sfc":
            {
                var r = SfcIO.Load(doc, path);
                var note = r.Skipped > 0 ? $"  ※未対応 {r.Skipped} 個: " + string.Join(", ", r.SkippedKinds.Select(k => $"{k.Key} {k.Value}")) : "";
                return new OpenResult(note, r.Sheet, r.ScaleDenominator, null, true);
            }
            case ".p21" or ".p2z":
            {
                var r = P21IO.Load(doc, path);
                var note = r.Skipped > 0 ? $"  ※未対応 {r.Skipped} 個: " + string.Join(", ", r.SkippedKinds.Select(k => $"{k.Key} {k.Value}")) : "";
                return new OpenResult(note, r.Sheet, r.ScaleDenominator, null, true);
            }
            case ".jww":
            {
                var r = JwwIO.Load(doc, path);
                var note = $"  Jw_cad のデータ版 {r.Version}。";
                if (r.Skipped > 0) note += "  ※" + string.Join(", ", r.SkippedKinds.Select(k => $"{k.Key} {k.Value}"));
                return new OpenResult(note, r.Sheet, r.ScaleDenominator, null, true);
            }
            case ".dwg":
            {
                var skipped = DwgIO.Load(doc, path, out var scale, out var unit);
                return new OpenResult(skipped > 0 ? $"  ※未対応の図形 {skipped} 個は読み込んでいません (寸法など)。ブロックは分解して読み込みました" : "", null, scale, unit);
            }
            default:
            {
                var skipped = DxfIO.Load(doc, path, out var scale, out var unit);
                return new OpenResult(skipped > 0 ? $"  ※未対応の図形 {skipped} 個は読み込んでいません (ブロック・寸法・ハッチング等)" : "", null, scale, unit);
            }
        }
    }

    /// <summary>
    /// 読み込んだ図形を、選んだ縮尺 (と DXF / DWG の座標の単位) で読み直したときの倍率。
    /// SFC / P21 / JWW と、縮尺の書いてある DXF / DWG は「用紙上の大きさのまま」(図寸固定): ファイルの縮尺と違う縮尺にすると実寸が縮尺の比で変わる。
    /// DXF / DWG の座標が用紙上の mm (図寸) なら、選んだ縮尺で実寸 (m) にする。
    /// </summary>
    public static double ImportFactor(OpenResult r, double scale, ImportUnit unit)
    {
        var ratio = r.FileScale is double fs && fs > 0 ? scale / fs : 1;
        if (r.PaperFormat) return ratio;
        return unit switch
        {
            ImportUnit.Millimeter => 0.001 * ratio,
            ImportUnit.Centimeter => 0.01 * ratio,
            ImportUnit.PaperMillimeter => scale / 1000,
            _ => ratio,
        };
    }

    /// <summary>
    /// 読み込んだ図面を、選んだ縮尺・単位にする (図形を原点を基準に拡大・縮小し、シートの縮尺を scale にする)。
    /// 文字・記号は用紙上の大きさのまま。戻り値は倍率と、用紙の範囲 (あれば)。
    /// </summary>
    public static (double Factor, Box2? Sheet) ApplyImportScale(CadDocument doc, OpenResult r, double scale, ImportUnit unit)
    {
        var k = ImportFactor(r, scale, unit);
        if (Math.Abs(k - 1) > 1e-12)
        {
            foreach (var e in doc.Entities)
            {
                if (e is SymbolEntity s)
                {
                    var mm = s.SizeMm;
                    s.Scale(Vec2.Zero, k);
                    s.SizeMm = mm;
                }
                else
                {
                    e.Scale(Vec2.Zero, k);
                }
            }
        }
        doc.ScaleDenominator = scale;
        foreach (var s in doc.Entities.OfType<SymbolEntity>()) s.Size = doc.PaperMmToMeters(s.SizeMm);
        doc.History.Clear();
        doc.NotifyChanged();
        var sheet = r.Sheet is Box2 b ? new Box2(b.Min * k, b.Max * k) : (Box2?)null;
        return (k, sheet);
    }

    /// <summary>拡張子の形式で保存し、ログに出す文を返す。</summary>
    public static string Save(CadDocument doc, string path)
    {
        // 画面で一定の大きさの文字は、ファイルには用紙上の大きさ (1px = 0.26mm) を縮尺で換算した高さで書く
        var screenTexts = doc.Entities.OfType<TextEntity>().Where(t => t.ScreenHeightPx != null).Select(t => (Text: t, t.Height)).ToList();
        foreach (var (t, _) in screenTexts) t.Height = doc.PaperMmToMeters(t.ScreenHeightPx!.Value * TextEntity.MmPerPixel);
        try
        {
            return SaveAs(doc, path);
        }
        finally
        {
            foreach (var (t, h) in screenTexts) t.Height = h;
        }
    }

    private static string SaveAs(CadDocument doc, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".sfc":
            {
                SfcWriter.Save(doc, path);
                var (paper, pw, ph, _) = SfcWriter.Layout(doc);
                return $"SFC で保存しました: {path}  (用紙 {PaperText(paper, pw, ph)} 横, 縮尺 {doc.ScaleText})";
            }
            case ".p21":
            {
                P21IO.Save(doc, path);
                var (paper, pw, ph, _) = SfcWriter.Layout(doc);
                return $"SXF (P21) で保存しました: {path}  (用紙 {PaperText(paper, pw, ph)} 横, 縮尺 {doc.ScaleText})";
            }
            case ".jww":
            {
                JwwIO.Save(doc, path);
                var (paper, pw, ph, _) = SfcWriter.Layout(doc);
                return $"Jw_cad (JWW) で保存しました: {path}  (用紙 {PaperText(paper, pw, ph)}, 縮尺 {doc.ScaleText})";
            }
            case ".dwg":
                DwgIO.Save(doc, path);
                return $"DWG (AutoCAD 2013 形式) で保存しました: {path}";
            default:
                DxfIO.Save(doc, path);
                return $"保存しました: {path}";
        }
    }

    /// <summary>画像を含む図面を保存したときの注意 (無ければ null)。画像を残せるのは DXF だけ (ファイルの場所を記録する)。</summary>
    public static string? ImageNote(CadDocument doc, string path)
    {
        var n = doc.Entities.OfType<ImageEntity>().Count();
        if (n == 0) return null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".dxf" or "")
            return "※ 画像は DXF に埋め込まず、画像ファイルの場所を記録しています。DXF を別の場所へ移すときは画像ファイルも同じフォルダに入れてください。";
        var name = ext switch { ".sfc" => "SFC", ".p21" => "SXF (P21)", ".jww" => "Jw_cad (JWW)", ".dwg" => "DWG", _ => ext.TrimStart('.').ToUpperInvariant() };
        return $"※ {name} には画像を保存できないため、画像 {n} 枚は保存していません (画像も残すには DXF で保存してください)。";
    }

    private static string PaperText(PaperSize? paper, double w, double h) => paper?.Name ?? $"自由 {w:0}×{h:0}mm";
}
