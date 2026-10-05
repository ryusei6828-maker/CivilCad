using System.IO;
using CivilCad.App.Drawing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>図面ファイルの形式 (拡張子) ごとの読み書きの振り分け。</summary>
public static class DrawingFiles
{
    /// <summary>開くダイアログのフィルタ。</summary>
    public const string OpenFilter =
        "図面ファイル (*.dxf;*.dwg;*.sfc)|*.dxf;*.dwg;*.sfc|DXF ファイル (*.dxf)|*.dxf|DWG ファイル (*.dwg)|*.dwg|SXF (SFC) ファイル (*.sfc)|*.sfc";

    /// <summary>保存ダイアログの形式 (拡張子とフィルタの表示名)。順番がフィルタの番号になる。</summary>
    public static IReadOnlyList<(string Ext, string Label)> SaveFormats { get; } =
    [
        (".dxf", "DXF ファイル (*.dxf)|*.dxf"),
        (".dwg", "DWG ファイル (AutoCAD 2013 形式) (*.dwg)|*.dwg"),
        (".sfc", "SXF (SFC) ファイル (*.sfc)|*.sfc"),
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
    public sealed record OpenResult(string Note, Box2? Sheet);

    /// <summary>ファイルを読み込んで doc を置き換える。</summary>
    public static OpenResult Open(CadDocument doc, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".sfc":
            {
                var r = SfcIO.Load(doc, path);
                var note = $"  縮尺 {doc.ScaleText} として実寸 (m) に換算しました。";
                if (r.Skipped > 0) note += $"  ※未対応 {r.Skipped} 個: " + string.Join(", ", r.SkippedKinds.Select(k => $"{k.Key} {k.Value}"));
                return new OpenResult(note, r.Sheet);
            }
            case ".dwg":
            {
                var skipped = DwgIO.Load(doc, path);
                return new OpenResult(skipped > 0 ? $"  ※未対応の図形 {skipped} 個は読み込んでいません (寸法・点など)。ブロックは分解して読み込みました" : "", null);
            }
            default:
            {
                var skipped = DxfIO.Load(doc, path);
                return new OpenResult(skipped > 0 ? $"  ※未対応の図形 {skipped} 個は読み込んでいません (ブロック・寸法・ハッチング等)" : "", null);
            }
        }
    }

    /// <summary>拡張子の形式で保存し、ログに出す文を返す。</summary>
    public static string Save(CadDocument doc, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".sfc":
            {
                SfcWriter.Save(doc, path);
                var (paper, pw, ph, _) = SfcWriter.Layout(doc);
                return $"SFC で保存しました: {path}  (用紙 {PaperText(paper, pw, ph)} 横, 縮尺 {doc.ScaleText})";
            }
            case ".dwg":
                DwgIO.Save(doc, path);
                return $"DWG (AutoCAD 2013 形式) で保存しました: {path}";
            default:
                DxfIO.Save(doc, path);
                return $"保存しました: {path}";
        }
    }

    private static string PaperText(PaperSize? paper, double w, double h) => paper?.Name ?? $"自由 {w:0}×{h:0}mm";
}
