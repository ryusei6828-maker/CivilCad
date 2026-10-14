using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>画像コマンドの中の操作。</summary>
public enum ImageMode
{
    /// <summary>画像・PDF を読み込んで配置する。</summary>
    Load,
    /// <summary>残す範囲を 2 点で指定してトリミングする。</summary>
    Crop,
    /// <summary>画像の上の 2 点を図面上の 2 点に合わせる (移動・回転・拡大縮小)。</summary>
    Align,
}

/// <summary>
/// 画像コマンド (IMG): 画像の読込・トリミング・2 点位置合わせをまとめたコマンド。
/// 画像をクリックして選び、作図入力バーのボタン (または I / C / A の入力・右クリック) で操作を選ぶ。
/// 各操作 (配置・トリミング・位置合わせ) を終えると、このコマンドに戻る (Esc で画像コマンドを終える)。
/// </summary>
public sealed class ImageTool : CadTool
{
    public const string Code = "IMG";

    /// <summary>読込 (ファイルを開く窓など)。MainWindow が設定する。</summary>
    public static Action? LoadRequested { get; set; }

    public override string Name => "画像";

    /// <summary>I / C / A / R をコマンド欄に打てるように、英字キー単独のショートカットを止める。</summary>
    public override bool WantsTextInput => true;

    /// <summary>新しい画像コマンド (各操作の後に戻る先)。</summary>
    public static CadTool Hub() => new ImageTool { CommandCode = Code };

    /// <summary>選んでいる画像 (1 枚だけのとき)。</summary>
    private ImageEntity? Selected => Canvas.Selection.OfType<ImageEntity>().ToList() is [var only] ? only : null;

    protected override void OnActivated() => SetPrompt(Selected is { } img
        ? $"画像「{System.IO.Path.GetFileName(img.Path)}」: トリミング (C) / 2点位置合わせ (A) / 読込 (I)。別の画像はクリックで選び直す"
        : "画像: 読込 (I)、またはトリミング・位置合わせする画像をクリック (C: トリミング / A: 2点位置合わせ)");

    public override void OnPoint(Vec2 p)
    {
        if (Canvas.HitTest(p) is ImageEntity img)
        {
            Canvas.SetSelection([img]);
            OnActivated();
        }
        else Canvas.ShowMessage("画像をクリックしてください (画像の枠か、画像の中)。新しく置くときは「読込」(I)。");
    }

    public override bool OnText(string text)
    {
        switch (text.Trim().ToUpperInvariant())
        {
            case "I": Start(Canvas, ImageMode.Load); return true;
            case "C": Start(Canvas, ImageMode.Crop); return true;
            case "A": Start(Canvas, ImageMode.Align); return true;
            case "R": ResetCrop(Canvas); return true;
            default: return false;
        }
    }

    public override IEnumerable<(string Label, Action Run)> MenuItems =>
    [
        ("画像・PDF を読み込む (I)", () => Start(Canvas, ImageMode.Load)),
        ("トリミング (C)", () => Start(Canvas, ImageMode.Crop)),
        ("トリミングを解除 (R)", () => ResetCrop(Canvas)),
        ("2点位置合わせ (A)", () => Start(Canvas, ImageMode.Align)),
    ];

    /// <summary>画像コマンドの中の操作を始める。終えると画像コマンドに戻る。</summary>
    public static void Start(CadCanvas canvas, ImageMode mode)
    {
        switch (mode)
        {
            case ImageMode.Load:
                if (LoadRequested is { } load) load();
                break;
            case ImageMode.Crop:
                // 画像を 1 枚選んでいればそれを、選んでいなければクリックで選ぶ
                Run(canvas, new ImageCropTool());
                break;
            case ImageMode.Align:
                if (canvas.Selection.Count == 0)
                {
                    canvas.ShowMessage("2点位置合わせ: 先に合わせる画像をクリックして選んでください。");
                    return;
                }
                Run(canvas, new AlignTool());
                break;
        }
    }

    /// <summary>操作のツールを、終えたら画像コマンドに戻るようにして始める。</summary>
    public static void Run(CadCanvas canvas, CadTool tool)
    {
        tool.CommandCode = Code;
        tool.ReturnTo = Hub;
        canvas.SetTool(tool);
    }

    /// <summary>選んでいる画像のトリミングを解除する (全体を表示に戻す)。</summary>
    public static void ResetCrop(CadCanvas canvas)
    {
        var images = canvas.Selection.OfType<ImageEntity>().Where(i => i.IsCropped).ToList();
        if (images.Count == 0)
        {
            canvas.ShowMessage("トリミングを解除する画像を選んでください (トリミングしている画像)。");
            return;
        }
        var pairs = images.Select(i =>
        {
            var full = (ImageEntity)i.Clone();
            full.SetCrop(0, 0, 1, 1);
            return (Old: (Entity)i, New: (Entity)full);
        }).ToList();
        canvas.Document.History.Execute(new ReplaceEntitiesCommand(pairs, "トリミングを解除"));
        canvas.SetSelection(pairs.Select(x => x.New));
        canvas.ShowMessage($"トリミングを解除しました ({pairs.Count} 枚)。");
        canvas.Tool.RefreshPrompt();
    }
}
