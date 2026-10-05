using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CivilCad.Alignment;
using CivilCad.App.Alignment;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using HorizontalAlignment = CivilCad.Alignment.HorizontalAlignment;

namespace CivilCad.App;

/// <summary>
/// シート (タブ 1 枚分)。図面と、ファイル名・路線データ・表示位置など図面ごとの状態を持つ。
/// </summary>
public sealed class Sheet : INotifyPropertyChanged
{
    private static int _untitledCounter;
    private string? _filePath;
    private bool _isActive;
    private long _savedVersion;

    public Sheet()
    {
        UntitledNumber = ++_untitledCounter;
        Doc.Changed += (_, _) => OnPropertyChanged(nameof(Title));
        Doc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CadDocument.ScaleDenominator) or nameof(CadDocument.PlaneZone))
            {
                OnPropertyChanged(nameof(Info));
                OnPropertyChanged(nameof(ScaleText));
                OnPropertyChanged(nameof(ZoneText));
            }
        };
    }

    public CadDocument Doc { get; } = new();
    public int UntitledNumber { get; }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(FileName));
        }
    }

    public string FileName => FilePath != null ? Path.GetFileName(FilePath) : $"無題{UntitledNumber}";

    /// <summary>タブの見出し (未保存の変更があれば *)。</summary>
    public string Title => FileName + (IsModified ? " *" : "");

    public string ScaleText => $"縮尺 {Doc.ScaleText}";
    public string ZoneText => Doc.PlaneZone is int n && PlaneRectangular.Find(n) is { } z ? $"座標系 {z.Name}" : "座標系 未設定";

    /// <summary>タブに小さく出す縮尺と系 (例: 1:500 ・ II系)。</summary>
    public string Info => Doc.ScaleText + (Doc.PlaneZone is int n && PlaneRectangular.Find(n) is { } z ? $" ・ {z.Name}" : "");

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    public bool IsModified => Doc.History.Version != _savedVersion;

    /// <summary>何も描かれておらず、保存も読み込みもしていない (開いたファイルの入れ物に使い回せる)。</summary>
    public bool IsBlank => FilePath == null && Doc.Entities.Count == 0 && !IsModified;

    public void MarkSaved()
    {
        _savedVersion = Doc.History.Version;
        OnPropertyChanged(nameof(Title));
    }

    // 図面ごとの作業状態
    public AlignmentInput AlignmentInput { get; set; } = AlignmentInput.Sample();
    public HorizontalAlignment? Alignment { get; set; }
    public Vec2? ViewCenter { get; set; }
    public double? ViewScale { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
