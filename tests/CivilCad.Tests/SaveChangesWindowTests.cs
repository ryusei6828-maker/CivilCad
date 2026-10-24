using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using CivilCad.App.Views;

namespace CivilCad.Tests;

/// <summary>終了時の確認 (Excel と同じ「このファイルの変更内容をプロジェクトファイルに保存しますか？」[保存] [保存しない] [キャンセル])。</summary>
public class SaveChangesWindowTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static SaveChangesAnswer Press(Func<SaveChangesWindow, Button?> button)
    {
        var w = new SaveChangesWindow(["Sheet1", "道路.dxf"]);
        if (button(w) is { } b) b.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        else w.Close(); // × で閉じる
        return w.Answer;
    }

    [Fact]
    public void Buttons_ReturnAnswer() => RunSta(() =>
    {
        Assert.Equal(SaveChangesAnswer.Save, Press(w => w.SaveButton));
        Assert.Equal(SaveChangesAnswer.DontSave, Press(w => w.DontSaveButton));
        Assert.Equal(SaveChangesAnswer.Cancel, Press(w => w.CancelButton));
        Assert.Equal(SaveChangesAnswer.Cancel, Press(_ => null));
    });

    [Fact]
    public void Wording_MatchesExcel() => RunSta(() =>
    {
        Assert.Equal("このファイルの変更内容をプロジェクトファイルに保存しますか？", SaveChangesWindow.Message);
        var w = new SaveChangesWindow(["Sheet1"]);
        Assert.Equal(["保存", "保存しない", "キャンセル"], new[] { w.SaveButton, w.DontSaveButton, w.CancelButton }.Select(b => (string)b.Content));
        Assert.True(w.SaveButton.IsDefault);   // Enter は保存
        Assert.True(w.CancelButton.IsCancel);  // Esc はキャンセル
        w.Close();
    });
}
