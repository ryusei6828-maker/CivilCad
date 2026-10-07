using System.Windows;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 画像のトリミング (IC): 画像を選んでから (または画像をクリックして)、残す範囲の対角の 2 点を指定する。
/// 範囲は画像の向きに沿った四角 (回転した画像でも画像の縦横に沿う)。トリミングで隠した所も、範囲を広く取り直せば戻せる。
/// 「トリミングを解除」で全体を表示に戻す。
/// </summary>
public sealed class ImageCropTool : CadTool
{
    private ImageEntity? _image;
    private Vec2? _first;
    private Vec2 _cursor;

    public override string Name => "画像のトリミング";
    public override Vec2? BasePoint => _first;
    public override bool CanStepBack => _first != null || _image != null;

    protected override void OnActivated()
    {
        if (_image == null && Canvas.Selection.OfType<ImageEntity>().ToList() is [var only]) _image = only;
        UpdatePrompt();
    }

    private void UpdatePrompt() => SetPrompt(_image == null ? "トリミングする画像をクリック"
        : _first == null ? "残す範囲の 1 点目 (四角の角) を指定 (R: トリミングを解除)"
        : "残す範囲の対角の点を指定");

    public override void StepBack()
    {
        if (_first != null) _first = null;
        else _image = null;
        UpdatePrompt();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_image == null)
        {
            if (Canvas.HitTest(p) is ImageEntity img) _image = img;
            else Canvas.ShowMessage("画像をクリックしてください (画像の枠か、画像の中)。");
            UpdatePrompt();
            return;
        }
        if (_first is not Vec2 a)
        {
            _first = p;
            UpdatePrompt();
            return;
        }
        var (s0, t0) = _image.Unmap(a);
        var (s1, t1) = _image.Unmap(p);
        var crop = (ImageEntity)_image.Clone();
        try
        {
            crop.SetCrop(s0, t0, s1, t1);
        }
        catch (ArgumentException ex)
        {
            Canvas.ShowMessage(ex.Message);
            _first = null;
            UpdatePrompt();
            return;
        }
        Replace(crop, "画像のトリミング");
        Canvas.ShowMessage("画像をトリミングしました (隠した所は「トリミングを解除」で戻せます)。");
        Finish();
    }

    public override bool OnText(string text)
    {
        if (_image == null || !text.Trim().Equals("R", StringComparison.OrdinalIgnoreCase)) return false;
        Reset();
        return true;
    }

    public override IEnumerable<(string Label, Action Run)> MenuItems => _image != null ? [("トリミングを解除", Reset)] : [];

    private void Reset()
    {
        if (_image is not { IsCropped: true } img)
        {
            Canvas.ShowMessage("この画像はトリミングしていません。");
            return;
        }
        var full = (ImageEntity)img.Clone();
        full.SetCrop(0, 0, 1, 1);
        Replace(full, "トリミングを解除");
        Canvas.ShowMessage("トリミングを解除しました。");
        Finish();
    }

    private void Replace(ImageEntity made, string name)
    {
        Execute(new ReplaceEntitiesCommand([(_image!, made)], name));
        Canvas.SetSelection([made]);
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_image is not { } img) return;
        // 画像全体の枠 (破線) と、残す範囲 (画像の向きに沿った四角)
        var full = img.FullCorners.Select(Canvas.WorldToScreen).ToArray();
        for (var i = 0; i < 4; i++) dc.DrawLine(CadCanvas.PreviewDashPen, full[i], full[(i + 1) % 4]);
        if (_first is not Vec2 a) return;
        var (s0, t0) = img.Unmap(a);
        var (s1, t1) = img.Unmap(_cursor);
        Point[] c = [Canvas.WorldToScreen(img.Map(s0, t0)), Canvas.WorldToScreen(img.Map(s1, t0)), Canvas.WorldToScreen(img.Map(s1, t1)), Canvas.WorldToScreen(img.Map(s0, t1))];
        for (var i = 0; i < 4; i++) dc.DrawLine(CadCanvas.PreviewPen, c[i], c[(i + 1) % 4]);
    }
}
