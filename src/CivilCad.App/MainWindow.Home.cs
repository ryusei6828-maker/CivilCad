using System.IO;
using System.Windows;
using System.Windows.Input;

namespace CivilCad.App;

/// <summary>
/// 「ホーム」: メニューの左端。押すと「ホーム」の下に、窓の縦横それぞれ半分 (面積で 1/4) ほどの大きさで、
/// Excel の「ファイル」のようなメニュー (新規・開く・保存・プロジェクト・PDF・読込・終了) と今の図面の情報を出す。
/// 戻るボタン・Esc・外をクリックで閉じる。Alt+F でも開く。(CivilCad3D のホームと同じ作り)
/// </summary>
public partial class MainWindow
{
    private bool BackstageOpen => Backstage.Visibility == Visibility.Visible;

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (HomeTab.IsChecked == true) ShowBackstage();
        else CloseBackstage();
    }

    /// <summary>ホームの大きさ: 窓の縦横それぞれ半分 (面積で 1/4)。小さい窓でも中身が入る大きさより小さくはしない。メニューの行のすぐ下に出す。</summary>
    private void LayoutBackstage()
    {
        BackstagePanel.Width = Math.Max(680, ActualWidth / 2);
        BackstagePanel.Height = Math.Max(470, ActualHeight / 2);
        BackstagePanel.Margin = new Thickness(2, MenuRow.ActualHeight, 0, 0);
    }

    /// <summary>ホームの外 (窓の残りの部分) をクリックしたら閉じる。ホームの中のクリックでは閉じない。</summary>
    private void BackstageOutside_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Backstage)) return;
        CloseBackstage();
        e.Handled = true;
    }

    private void ShowBackstage()
    {
        RefreshBackstage();
        LayoutBackstage();
        Backstage.Visibility = Visibility.Visible;
        HomeTab.IsChecked = true;
        BackstageBack.Focus();
    }

    private void CloseBackstage()
    {
        Backstage.Visibility = Visibility.Collapsed;
        HomeTab.IsChecked = false;
    }

    /// <summary>ホームのキー: 開いているときの Esc で閉じる、Alt+F で開く・閉じる。処理したら true。</summary>
    private bool BackstageKey(KeyEventArgs e)
    {
        if (BackstageOpen && e.Key == Key.Escape)
        {
            CloseBackstage();
            return true;
        }
        if (e.Key == Key.System && e.SystemKey == Key.F && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (BackstageOpen) CloseBackstage();
            else ShowBackstage();
            return true;
        }
        return false;
    }

    private void BackstageBack_Click(object sender, RoutedEventArgs e) => CloseBackstage();

    // ファイルの操作は、先にホームを閉じてから (ダイアログが重なって見えないように) 今までと同じ処理を呼ぶ
    private void BackstageNew_Click(object sender, RoutedEventArgs e) { CloseBackstage(); ApplicationCommands.New.Execute(null, this); }

    private void BackstageOpen_Click(object sender, RoutedEventArgs e) { CloseBackstage(); ApplicationCommands.Open.Execute(null, this); }

    private void BackstageSave_Click(object sender, RoutedEventArgs e) { CloseBackstage(); ApplicationCommands.Save.Execute(null, this); }

    /// <summary>コマンド (PJO・PJS・PJA・PDF・PI・IMG) を実行する。コマンドは CommandParameter に入れておく。</summary>
    private void BackstageTool_Click(object sender, RoutedEventArgs e)
    {
        CloseBackstage();
        if (sender is System.Windows.Controls.Button { CommandParameter: string code }) RunCommand(code);
    }

    private void BackstageExit_Click(object sender, RoutedEventArgs e) => Exit_Click(sender, e);

    /// <summary>今の図面 (シート) の情報を書き直す。</summary>
    private void RefreshBackstage()
    {
        BackstageName.Text = _active.FileName + (_active.IsModified ? " *" : "");
        BackstagePath.Text = _filePath ?? "まだ保存していません (「名前を付けて保存」で図面のファイルにします)";
        var rows = new List<(string Label, string Value)>
        {
            ("シート", $"{_sheets.IndexOf(_active) + 1} / {_sheets.Count} 枚"),
            ("図形", $"{_doc.Entities.Count:N0} 個 (レイヤ {_doc.Layers.Count})"),
            ("縮尺", _doc.ScaleText),
            ("座標系", _active.ZoneText.Replace("座標系 ", "")),
            ("プロジェクト", _projectPath != null ? Path.GetFileName(_projectPath) : "なし"),
            ("保存", _active.IsModified ? "保存していない変更があります" : "保存済み"),
        };
        if (_filePath != null && File.Exists(_filePath))
            rows.Add(("最終保存", File.GetLastWriteTime(_filePath).ToString("yyyy/MM/dd HH:mm")));
        BackstageLabels.Text = string.Join(Environment.NewLine, rows.Select(r => r.Label));
        BackstageInfo.Text = string.Join(Environment.NewLine, rows.Select(r => r.Value));
    }
}
