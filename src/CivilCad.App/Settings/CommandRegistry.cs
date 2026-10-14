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
        new("REC", "矩形", "IconRect", "1 つ目の角 → 対角の角 (「@幅,高さ」でも可) で四角を描く。続けて描ける"),
        new("PO", "点", "IconPoint", "クリックか座標の入力 (x,y / @dx,dy / s X,Y) で点を置く。続けて置ける"),
        new("PE", "垂線", "IconPerpendicular", "基準の線 → 線上の点 → 終点 の順に指定"),
        new("T", "文字", "IconText"),
        new("DIM", "寸法", "IconDimension", "1 点目 → 2 点目 → 寸法線の位置。続けてクリックで直列寸法。向き (平行・水平・垂直)・端末・単位は入力バー"),
        new("LD", "引き出し線", "IconLeader", "先端 → 折れ点 (いくつでも) → Enter → 文字 (行ごとに Enter、空の Enter で確定。文字なしも可)"),
        new("HA", "旗揚げ", "IconFlag", "先端 → 旗竿の始まり → 文字 (行ごとに Enter、空の Enter で確定)。旗竿は文字の幅で自動"),
        new("FI", "塗りつぶし", "IconFill", "閉じた図形を選択してから実行、または内側をクリック"),
        new("PS", "プリセット (方位記号など)", "IconPreset", "記号を選んでクリックで配置。大きさは用紙上の mm"),
        new("PSR", "プリセット登録", "IconPresetAdd", "図形を選択してから実行。基準点と名前を指定すると、プリセット (PS) で置けるようになる"),
        new("NM", "方位記号の向き (手動)", "IconPreset", "方位記号を選択して実行。N を向ける方向をクリック、または角度を入力。移動・回転しても北に合わせ直さなくなる"),
        new("NA", "方位記号を北に合わせる (自動セット)", "IconPreset", "選択した方位記号 (選択が無ければ手動にした全部) を北に向け、以後も自動で合わせる"),
        new("J", "結合", "IconJoin", "線・ポリライン・円弧を選択して実行。同じ直線上の線・同じ円上の円弧はまとめ、端点がつながる線は 1 本のポリラインになる"),
        new("H", "ハッチング", "IconHatch", "閉じた図形の内側をクリック、または点を指定して Enter"),
        new("FR", "図枠 (外枠・表題欄)", "IconFrame"),
        new("SC", "縮尺設定", "IconScale", "文字高・図枠・路線注記の換算に使う"),
        new("CS", "座標設定", "IconCoord", "基準点を測量座標に合わせて図面を移動・回転"),
        new("CM", "座標管理 開く/閉じる", "IconPoints", "登録された座標 (点名・X・Y・Z) の一覧。座標読込の点・幅杭・点コマンドの点"),
        new("CEX", "座標を CSV に書き出す", "IconSave", "座標管理の一覧で選んでいれば選んだ点だけ"),
        new("SEX", "座標を SIMA に書き出す", "IconSave", "座標管理の一覧で選んでいれば選んだ点だけ"),
        new("PI", "座標読込 (SIMA / CSV)", "IconPoints", "座標ファイルの点を、点の印と点名で図面に配置"),
        new("IM", "画像読込 (JPG / PNG / TIFF / PDF)", "IconImage", "ワールドファイル (.jgw/.tfw など) があればその位置に、無ければ位置と幅を指定して配置。PDF は Enter で原寸"),
        new("IC", "画像のトリミング", "IconCrop", "画像を選んで (またはクリックして)、残す範囲の対角の 2 点を指定。R でトリミングを解除"),
        new("DF", "最前面へ", "IconBringFront", "選択した図形を一番上に表示。画像は線・文字より上になる"),
        new("DB", "最背面へ", "IconSendBack", "選択した図形を一番下に表示。画像は線・塗りの下になる"),
        new("PDF", "PDF に印刷", null, "用紙・縮尺・白黒を選んで PDF に書き出す"),
        new("ZN","平面直角座標系の系番号", "IconCoord", "I〜XIX系。緯度経度の表示に使う"),
        new("SEL", "選択モード", "IconSelect", "コマンドを終えて選ぶ状態に戻る。クリックで 1 つ選択、ドラッグで範囲選択、Shift+クリックで追加・解除 (Esc でも同じ)"),
        new("M", "移動", "IconMove", "先に図形を選択"),
        new("SCL", "拡大縮小", "IconResize", "先に図形を選択。基点 → 倍率 (または 参照する長さ → 新しい長さ)"),
        new("AL", "2 点指示 (位置合わせ)", "IconAlign", "先に図形 (画像など) を選択。元の点 → 合わせ先 を 2 組指定して、移動・回転 (・拡大縮小) で合わせる"),
        new("CO", "複写", "IconCopy", "先に図形を選択"),
        new("RO", "回転", "IconRotate", "先に図形を選択。中心点 → 基準の方向点 → 回転先の方向点 (角度の入力も可、反時計回りが +)。C で元を残す"),
        new("O", "オフセット", "IconOffset", "距離を入力 → 図形 → ずらす側をクリック"),
        new("TR", "トリム", "IconTrim", "境界を選んで Enter → 切り取る部分をクリック。Shift+クリックで延長"),
        new("EX", "延長", "IconExtend", "境界を選んで Enter → 伸ばす端をクリック。Shift+クリックでトリム"),
        new("ED", "文字変更", "IconText", "文字をクリックして中身を変える (選択中は文字のダブルクリックでも)"),
        new("E", "削除モード", "IconErase", "クリック (ドラッグで範囲) で消す図形を選び、ダブルクリックか Enter で削除。Esc で終了"),
        new("DI", "距離・方向角", "IconDistance"),
        new("ID", "座標計測", "IconCoord", "クリックした点の測量座標 X・Y を表示し、座標管理に登録 (点名を入力すると直前の点の名前を変える)"),
        new("AA", "面積", "IconArea"),
        new("RL", "路線作成 (路線パネル)", "IconRoute", "始点 → IP → 終点 を表で入れる (図面から・手入力・Excel から貼り付け)。計算して通過点 (No.杭の間隔・中間杭) を選んで作図"),
        new("RLF", "路線パネル フロート/ドッキング", "IconRoute", "別ウィンドウに切り離す / 左側に戻す"),
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
        new("GR", "グリッド 表示切替", null, "間隔は右下のグリッド欄、背景色は設定の「表示」タブ"),
        new("ORTHO", "直交モード 切替", null),
    ];

    public static IReadOnlyDictionary<string, string> DefaultBindings { get; } = new Dictionary<string, string>
    {
        ["SNAP"] = "F3",
        ["GR"] = "F7",
        ["ORTHO"] = "F8",
        ["PDF"] = "Ctrl+P",
    };

    public static CommandInfo? Find(string code) => All.FirstOrDefault(c => c.Code == code);
}
