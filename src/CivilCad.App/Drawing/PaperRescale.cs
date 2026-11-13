using CivilCad.App.Tools;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Drawing;

/// <summary>シートの縮尺を変えるときの、図形の扱い。</summary>
public enum ScaleChangeMode
{
    /// <summary>
    /// 図面をそのまま (図寸固定): 用紙上の大きさを保つ。図形を縮尺の比で拡大・縮小するので、画面の見た目は変わらず、
    /// 計測の値が新しい縮尺の値になる (1:100 で 10m の線は 1:250 で 25m)。基準は、座標設定をした図面なら測量座標の基準点
    /// (SurveyPivot。拡大しても図面と測量座標のずれが出ない)、していなければ原点 (0,0)。
    /// 測量座標の位置に置く物 (登録した座標の点・そのマークと点名・路線) は位置を動かさない:
    /// 点は何もせず、マーク・点名は点の位置を基準に (用紙上の大きさを保つ)、路線 (Tag "ALIGN:") は文字の高さだけ変える。
    /// </summary>
    KeepPaper,

    /// <summary>実寸を保つ (実寸固定): 図形の実寸 (計測の値) はそのまま。図枠だけ新しい縮尺の大きさに作り直す。</summary>
    KeepReal,
}

/// <summary>シートの縮尺の変更。どちらのやり方でも、表題欄の縮尺の文字は新しい縮尺に書き換える。</summary>
public static class PaperRescale
{
    /// <summary>
    /// 縮尺を変えるコマンド (元に戻すで一度に戻る)。scaleView を渡すと、図面をそのまま (図寸固定) で図形を k 倍したとき・
    /// 元に戻したときに、表示も k 倍 / 1/k 倍して見た目を変えない。
    /// </summary>
    public static IUndoableCommand Command(CadDocument doc, double newScale, ScaleChangeMode mode, Action<double>? scaleView = null)
    {
        var set = new SetScaleCommand(newScale);
        if (doc.ScaleDenominator <= 0) return set;
        var k = newScale / doc.ScaleDenominator;
        var pairs = Rescaled(doc, newScale, mode);
        IUndoableCommand command = pairs.Count == 0 ? set : new CompositeCommand(set.Name, new ReplaceEntitiesCommand(pairs, set.Name), set);
        return mode == ScaleChangeMode.KeepPaper && scaleView != null && pairs.Count > 0 ? new ViewScaledCommand(command, k, scaleView) : command;
    }

    private sealed class ViewScaledCommand(IUndoableCommand inner, double k, Action<double> scaleView) : IUndoableCommand
    {
        public string Name => inner.Name;

        public void Do(CadDocument doc)
        {
            inner.Do(doc);
            scaleView(k);
        }

        public void Undo(CadDocument doc)
        {
            inner.Undo(doc);
            scaleView(1 / k);
        }
    }

    /// <summary>作り直す図形 (元, 新しい縮尺での形)。</summary>
    public static List<(Entity Old, Entity New)> Rescaled(CadDocument doc, double newScale, ScaleChangeMode mode)
    {
        var k = newScale / doc.ScaleDenominator;
        var pairs = new List<(Entity, Entity)>();
        if (Math.Abs(k - 1) < 1e-12) return pairs;
        var oldText = $"1:{doc.ScaleDenominator:0.##}";
        var newText = $"1:{newScale:0.##}";

        // 実寸を保つときの図枠: 外枠ごとに、外枠の中の図枠の図形をまとめて外枠の左下を基準に拡大・縮小
        var outers = doc.Entities.OfType<PolylineEntity>()
            .Where(p => p.Tag == DrawingFrame.Tag && p.Layer.Name == DrawingFrame.LayerFrame)
            .Select(p => p.Bounds).ToList();
        // 座標設定をしてあれば (基準点が原点でない・向きがある) その基準点を中心に拡大する
        var bodyOrigin = doc.SurveyRotation != 0 || doc.SurveyPivot != Vec2.Zero ? doc.SurveyPivot : Vec2.Zero;
        var registered = mode == ScaleChangeMode.KeepPaper ? RegisteredCoordinates.Anchors(doc) : [];
        foreach (var e in doc.Entities)
        {
            Vec2 origin;
            if (mode == ScaleChangeMode.KeepPaper)
            {
                if (RegisteredCoordinates.IsPoint(e)) continue; // 登録した座標の点は測量座標の位置のまま
                if (registered.TryGetValue(e, out var anchor)) origin = anchor; // マーク・点名は点の位置を基準に
                else if (e.Tag?.StartsWith("ALIGN:", StringComparison.Ordinal) == true)
                {
                    if (e is not TextEntity at) continue; // 路線の線形は測量座標のまま
                    origin = at.Position; // 路線の文字は位置を変えず、用紙上の大きさだけ保つ
                }
                else origin = bodyOrigin;
            }
            else if (e.Tag == DrawingFrame.Tag)
            {
                var b = e.Bounds;
                var outer = outers.Where(o => o.Inflate(1e-6 * Math.Max(o.Width, o.Height)).Contains(b)).Cast<Box2?>().FirstOrDefault();
                origin = (outer ?? b).Min;
            }
            else continue;

            var made = e.Clone();
            if (made is TextEntity { ScreenHeightPx: not null } ft)
                ft.Position = origin + (ft.Position - origin) * k; // 画面で一定の大きさの文字は大きさを変えない
            else
                made.Scale(origin, k);
            if (made is SymbolEntity sym && e is SymbolEntity src) sym.SizeMm = src.SizeMm; // 記号の用紙上の大きさ (mm) は変えない
            if (made is TextEntity t && e.Tag == DrawingFrame.Tag && t.Text == oldText) t.Text = newText; // 表題欄の縮尺
            pairs.Add((e, made));
        }
        return pairs;
    }
}
