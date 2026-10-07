using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Drawing;

/// <summary>
/// シートの縮尺を変えるとき、「用紙上の大きさ (mm) で決まる物」を新しい縮尺に合わせて作り直す。
/// 図枠 (外枠の左下を基準に拡大・縮小、表題欄の縮尺の文字も書き換え)。
/// 図枠以外の文字・線・円などはそのまま (画面上の大きさも計測の値も変わらない)。
/// </summary>
public static class PaperRescale
{
    /// <summary>縮尺を変えるコマンド。keepPaperSize なら図枠も合わせる (元に戻すで一度に戻る)。</summary>
    public static IUndoableCommand Command(CadDocument doc, double newScale, bool keepPaperSize)
    {
        var set = new SetScaleCommand(newScale);
        if (!keepPaperSize || doc.ScaleDenominator <= 0) return set;
        var pairs = Rescaled(doc, newScale);
        return pairs.Count == 0 ? set : new CompositeCommand(set.Name, new ReplaceEntitiesCommand(pairs, set.Name), set);
    }

    /// <summary>作り直す図形 (元, 新しい縮尺での形)。</summary>
    public static List<(Entity Old, Entity New)> Rescaled(CadDocument doc, double newScale)
    {
        var k = newScale / doc.ScaleDenominator;
        var pairs = new List<(Entity, Entity)>();
        if (Math.Abs(k - 1) < 1e-12) return pairs;
        var oldText = $"1:{doc.ScaleDenominator:0.##}";
        var newText = $"1:{newScale:0.##}";

        // 図枠: 外枠ごとに、外枠の中の図枠の図形をまとめて外枠の左下を基準に拡大・縮小
        var outers = doc.Entities.OfType<PolylineEntity>()
            .Where(p => p.Tag == DrawingFrame.Tag && p.Layer.Name == DrawingFrame.LayerFrame)
            .Select(p => p.Bounds).ToList();
        foreach (var e in doc.Entities)
        {
            Entity? made = null;
            if (e.Tag == DrawingFrame.Tag)
            {
                var b = e.Bounds;
                var outer = outers.Where(o => o.Inflate(1e-6 * Math.Max(o.Width, o.Height)).Contains(b)).Cast<Box2?>().FirstOrDefault();
                var origin = (outer ?? b).Min;
                made = e.Clone();
                if (made is TextEntity { ScreenHeightPx: not null } ft)
                    ft.Position = origin + (ft.Position - origin) * k; // 画面で一定の文字は大きさを変えない
                else
                    made.Scale(origin, k);
                if (made is TextEntity t && t.Text == oldText) t.Text = newText; // 表題欄の縮尺
            }
            if (made != null) pairs.Add((e, made));
        }
        return pairs;
    }
}
