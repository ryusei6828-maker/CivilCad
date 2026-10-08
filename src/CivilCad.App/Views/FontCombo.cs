using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using CivilCad.Core.Model;

namespace CivilCad.App.Views;

/// <summary>文字のフォントを選ぶ一覧。作図入力バーとプロパティで使う。</summary>
public static class FontCombo
{
    private static List<string>? _names;

    /// <summary>よく使う日本語フォント (入っていれば一覧の先頭に出す)。</summary>
    private static readonly string[] Favorites =
    [
        TextEntity.DefaultFontName, "ＭＳ 明朝", "ＭＳ Ｐゴシック", "ＭＳ Ｐ明朝", "BIZ UDゴシック", "BIZ UD明朝",
        "游ゴシック", "游明朝", "メイリオ", "UD デジタル 教科書体 N-R",
    ];

    /// <summary>この PC のフォント名 (日本語名があれば日本語名)。よく使うものを先に、あとは名前順。</summary>
    public static IReadOnlyList<string> Names => _names ??= Load();

    private static List<string> Load()
    {
        var ja = XmlLanguage.GetLanguage("ja-JP");
        var all = new SortedSet<string>(StringComparer.CurrentCulture);
        foreach (var f in Fonts.SystemFontFamilies)
        {
            var name = f.FamilyNames.TryGetValue(ja, out var j) ? j : f.Source;
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')) all.Add(name);
        }
        var list = Favorites.Where(f => f == TextEntity.DefaultFontName || all.Contains(f)).ToList();
        list.AddRange(all.Where(n => !list.Contains(n)));
        return list;
    }

    /// <summary>current が null なら既定のフォント、"" なら未選択 (いくつかのフォントが混ざっているとき)。</summary>
    public static ComboBox Create(string? current, Action<string?> set, double width = 170)
    {
        var names = Names.ToList();
        var value = current ?? TextEntity.DefaultFontName;
        if (value.Length > 0 && !names.Contains(value)) names.Insert(0, value); // この PC に無いフォントも表示
        var box = new ComboBox
        {
            Width = width,
            ItemsSource = names,
            SelectedItem = value.Length > 0 ? value : null,
            IsTextSearchEnabled = true,
            ToolTip = "文字のフォント。この PC に無いフォントは ＭＳ ゴシックで表示する",
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not string name) return;
            box.FontFamily = new FontFamily(name + ", MS Gothic");
            set(name == TextEntity.DefaultFontName ? null : name);
        };
        if (value.Length > 0) box.FontFamily = new FontFamily(value + ", MS Gothic");
        return box;
    }
}
