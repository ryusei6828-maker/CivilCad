using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 文字をその場で直している間の状態 (画面には出ない)。文字・文字高・揃え・幅指定を持ち、
/// 入力欄を置く基準点を決め、確定するときの <see cref="TextEditResult"/> を作る。
/// </summary>
public sealed class InPlaceTextEdit
{
    private readonly CadDocument _doc;
    private readonly double _originalMm;
    private string _text;
    private double _heightM;
    private TextHAlign _hAlign;
    private TextVAlign _vAlign;
    private bool _keepFit;

    public InPlaceTextEdit(TextEntity original, CadDocument doc)
    {
        Original = original;
        _doc = doc;
        _text = original.Text;
        _heightM = original.Height;
        _hAlign = original.HAlign;
        _vAlign = original.VAlign;
        _originalMm = Math.Round(original.Height / doc.ScaleDenominator * 1000, 3);
    }

    /// <summary>直している元の文字 (確定するまで図面はこのまま)。</summary>
    public TextEntity Original { get; }

    /// <summary>設定が変わった。</summary>
    public event EventHandler? Changed;

    public string Text
    {
        get => _text;
        set
        {
            var s = value.Replace("\r\n", "\n").Replace('\r', '\n');
            if (s == _text) return;
            _text = s;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsMultiLine => _text.Contains('\n');

    /// <summary>文字の高さ (m)。画面で一定の文字は元のまま。</summary>
    public double HeightM => _heightM;

    /// <summary>文字高を直せるか (画面で一定の文字は直せない)。</summary>
    public bool HeightEditable => Original.ScreenHeightPx == null;

    /// <summary>用紙上の文字高 (mm)。</summary>
    public double HeightMm => Math.Round(_heightM / _doc.ScaleDenominator * 1000, 3);

    /// <summary>文字高を mm で決める。0 以下・数でないときは false。触っていない (元の mm のまま) なら元の高さを保つ。</summary>
    public bool SetHeightMm(double mm)
    {
        if (!HeightEditable || !(mm > 0) || double.IsInfinity(mm)) return false;
        var h = Math.Abs(mm - _originalMm) < 1e-9 ? Original.Height : _doc.PaperMmToMeters(mm);
        if (h == _heightM) return true;
        _heightM = h;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public TextHAlign HAlign
    {
        get => _hAlign;
        set
        {
            if (_hAlign == value) return;
            _hAlign = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public TextVAlign VAlign => _vAlign;

    /// <summary>縦の揃えを決める (元が文字の下端 (Baseline) で「下」を選んだときは、そのまま Baseline)。</summary>
    public void SetVAlign(TextVAlign v)
    {
        if (v == TextVAlign.Baseline) v = TextVAlign.Bottom;
        if (v == TextVAlign.Bottom && Original.VAlign == TextVAlign.Baseline) v = TextVAlign.Baseline;
        if (_vAlign == v) return;
        _vAlign = v;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>元に幅指定 (DXF などの決まった幅に伸ばす) があるか。</summary>
    public bool HasFitWidth => Original.FitWidth != null;

    /// <summary>「元の幅に合わせる」を選べるか (幅指定があり、1 行のとき)。</summary>
    public bool KeepFitEnabled => HasFitWidth && !IsMultiLine;

    /// <summary>「元の幅に合わせる」のチェック (既定はオフ)。</summary>
    public bool KeepFit
    {
        get => _keepFit;
        set
        {
            if (_keepFit == value) return;
            _keepFit = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>実際に幅指定を残すか。</summary>
    public bool EffectiveKeepFit => KeepFitEnabled && _keepFit;

    /// <summary>入力欄の基準点 (図面の座標)。揃えを変えると、元の文字の枠の左・中央・右 / 下・中・上に来る。</summary>
    public Vec2 Anchor => TextEditing.AnchorFor(Original, _hAlign, _vAlign);

    /// <summary>確定するときの内容。</summary>
    public TextEditResult Result => new(_text, _heightM, _hAlign, _vAlign, EffectiveKeepFit);
}
