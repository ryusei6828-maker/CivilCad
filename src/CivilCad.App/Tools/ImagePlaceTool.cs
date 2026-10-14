using System.IO;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 位置情報 (ワールドファイル) の無い画像の配置。
/// 左下の点 → 下辺の向きと幅 (2 点目のクリック、または幅を数値で入力) の順に指定する。縦横比は画像のまま。
/// Enter で既定の大きさ (画像 1 画素 = 用紙 0.1mm、PDF は用紙の原寸 = 図面の縮尺でページの大きさ) にする。
/// </summary>
public sealed class ImagePlaceTool(string path, int pixelWidth, int pixelHeight, int page = 0, double? paperWidthMm = null) : CadTool
{
    /// <summary>既定の大きさ: 画像 1 画素あたりの用紙上の mm。</summary>
    public const double DefaultMmPerPixel = 0.1;

    private Vec2? _origin;
    private Vec2 _cursor;

    public override string Name => "画像の配置";
    public override Vec2? BasePoint => _origin;
    public override bool WantsTextInput => _origin != null;
    public override bool CanStepBack => _origin != null;

    protected override void OnActivated() => UpdatePrompt();

    private void UpdatePrompt() => SetPrompt(_origin == null
        ? paperWidthMm != null
            ? $"PDF「{Path.GetFileName(path)}」{page + 1} ページの左下の位置を指定"
            : $"画像「{Path.GetFileName(path)}」({pixelWidth}×{pixelHeight}) の左下の位置を指定"
        : $"下辺の向きと幅を指定 (幅を m で入力も可 / Enter: {(paperWidthMm != null ? $"原寸 ({Doc.ScaleText} で用紙 {paperWidthMm:0}mm)" : "既定の大きさ")} {DefaultWidth():0.###}m)");

    private double DefaultWidth() => paperWidthMm is double mm ? Doc.PaperMmToMeters(mm) : Doc.PaperMmToMeters(DefaultMmPerPixel) * pixelWidth;

    public override void StepBack()
    {
        _origin = null;
        UpdatePrompt();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_origin is not Vec2 o)
        {
            _origin = p;
            UpdatePrompt();
            return;
        }
        if ((p - o).Length < 1e-9) return;
        Place(p - o);
    }

    public override void OnEnter()
    {
        if (_origin == null) Finish();
        else Place(new Vec2(DefaultWidth(), 0));
    }

    public override bool OnText(string text)
    {
        if (_origin == null || !TryParseNumber(text, out var width) || width <= 0) return false;
        Place(new Vec2(width, 0));
        return true;
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    private ImageEntity Make(Vec2 bottom)
    {
        var u = bottom;
        var v = u.LeftNormal * ((double)pixelHeight / pixelWidth);
        return new ImageEntity(Doc.CurrentLayer, path, pixelWidth, pixelHeight, _origin!.Value, u, v) { Page = page };
    }

    private void Place(Vec2 bottom)
    {
        var img = Make(bottom);
        Execute(new AddEntitiesCommand([img], "画像の配置"));
        Canvas.SetSelection([img]);
        Canvas.ShowMessage($"{(paperWidthMm != null ? $"PDF {page + 1} ページ" : "画像")}「{Path.GetFileName(path)}」を配置しました (幅 {img.Width:0.###}m × 高さ {img.Height:0.###}m)。");
        Finish();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_origin is not Vec2 o || (_cursor - o).Length < 1e-9) return;
        var c = Make(_cursor - o).Corners.Select(Canvas.WorldToScreen).ToArray();
        for (var i = 0; i < 4; i++) dc.DrawLine(CadCanvas.PreviewDashPen, c[i], c[(i + 1) % 4]);
        dc.DrawLine(CadCanvas.PreviewPen, c[0], c[1]);
    }
}
