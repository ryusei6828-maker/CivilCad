using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Input;
using CivilCad.App.Views;

namespace CivilCad.Tests;

/// <summary>終了時の確認 (Excel と同じ「このファイルの変更内容をプロジェクトファイルに保存しますか？」[保存(S)] [保存しない(N)] [キャンセル])。</summary>
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
        Assert.Equal(["保存(_S)", "保存しない(_N)", "キャンセル"], new[] { w.SaveButton, w.DontSaveButton, w.CancelButton }.Select(b => (string)b.Content));
        Assert.True(w.SaveButton.IsDefault);   // Enter は保存
        Assert.True(w.CancelButton.IsCancel);  // Esc はキャンセル
        w.Close();
    });

    private static SaveChangesAnswer Shortcut(Key key, ModifierKeys modifiers, bool expectHandled)
    {
        var w = new SaveChangesWindow(["Sheet1"]);
        Assert.Equal(expectHandled, w.HandleShortcut(key, modifiers));
        return w.Answer;
    }

    [Fact]
    public void Shortcuts_SAndN_WorkWithOrWithoutAlt() => RunSta(() =>
    {
        Assert.Equal(SaveChangesAnswer.Save, Shortcut(Key.S, ModifierKeys.None, true));
        Assert.Equal(SaveChangesAnswer.Save, Shortcut(Key.S, ModifierKeys.Alt, true));
        Assert.Equal(SaveChangesAnswer.DontSave, Shortcut(Key.N, ModifierKeys.None, true));
        Assert.Equal(SaveChangesAnswer.DontSave, Shortcut(Key.N, ModifierKeys.Alt, true));
    });

    [Fact]
    public void Shortcuts_OtherKeys_AreIgnored() => RunSta(() =>
    {
        Assert.Equal(SaveChangesAnswer.Cancel, Shortcut(Key.S, ModifierKeys.Control, false));
        Assert.Equal(SaveChangesAnswer.Cancel, Shortcut(Key.N, ModifierKeys.Control | ModifierKeys.Alt, false));
        Assert.Equal(SaveChangesAnswer.Cancel, Shortcut(Key.A, ModifierKeys.None, false));
    });
}
