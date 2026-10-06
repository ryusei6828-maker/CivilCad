namespace CivilCad.App.Settings;

/// <summary>キーを割り当てられるコマンド。Code は MainWindow.RunCommand に渡す名前。</summary>
public sealed record CommandInfo(string Code, string Name, string? IconKey, string? Note = null);

public static class CommandRegistry
{
    public static IReadOnlyList<CommandInfo> All { get; } =
    [
        new("L", "線", "IconLine"),
        new("PL", "ポリライン", "IconPolyline"),
        new("C", "円", "IconCircle"),
        new("A", "円弧 (3点)", "IconArc"),
        new("PE", "垂線", "IconPerpendicular", "基準の線 → 線上の点 → 終点 の順に指定"),
        new("T", "文字", "IconText"),
        new("H", "ハッチング", "IconHatch", "閉じた図形の内側をクリック、または点を指定して Enter"),
        new("FR", "図枠 (外枠・表題欄)", "IconFrame"),
        new("SC", "縮尺設定", "IconScale", "文字高・図枠・路線注記の換算に使う"),
        new("CS", "座標設定", "IconCoord", "基準点を測量座標に合わせて図面を移動・回転"),
        new("PI", "座標読込 (SIMA / CSV)", "IconPoints", "座標ファイルの点を、点の印と点名で図面に配置"),
        new("ZN","平面直角座標系の系番号", "IconCoord", "I〜XIX系。緯度経度の表示に使う"),
        new("M", "移動", "IconMove", "先に図形を選択"),
        new("CO", "複写", "IconCopy", "先に図形を選択"),
        new("E", "削除モード", "IconErase", "クリックした図形を消す。Esc で終了"),
        new("DI", "距離・方向角", "IconDistance"),
        new("AA", "面積", "IconArea"),
        new("RC", "路線計算 (IP法)", "IconRoute"),
        new("RX", "路線 XML (LandXML) 読込", "IconRoute", "J-LandXML の平面線形を IP 表にして路線計算を開く"),
        new("ST", "測点照会", "IconStation"),
        new("ZE", "全体表示", "IconZoomExtents"),
        new("LA", "レイヤパネル 開く/閉じる", "IconLayers"),
        new("LAF", "レイヤパネル フロート/ドッキング", "IconLayers", "別ウィンドウに切り離す / 左側に戻す"),
        new("PR", "プロパティ 開く/閉じる", "IconProperties"),
        new("PRF", "プロパティ フロート/ドッキング", "IconProperties", "別ウィンドウに切り離す / 右側に戻す"),
        new("U", "元に戻す", "IconUndo", "Ctrl+Z でも可"),
        new("REDO", "やり直し", "IconRedo", "Ctrl+Y でも可"),
        new("SNAP", "スナップ 切替", null),
        new("ORTHO", "直交モード 切替", null),
    ];

    public static IReadOnlyDictionary<string, string> DefaultBindings { get; } = new Dictionary<string, string>
    {
        ["SNAP"] = "F3",
        ["ORTHO"] = "F8",
    };

    public static CommandInfo? Find(string code) => All.FirstOrDefault(c => c.Code == code);
}
