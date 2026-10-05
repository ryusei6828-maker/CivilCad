using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CivilCad.Core.Model;

public sealed class Layer(string name, RgbColor color) : INotifyPropertyChanged
{
    private string _name = name;
    private RgbColor _color = color;
    private bool _isVisible = true;
    private bool _isLocked;
    private bool _isCurrent;

    public string Name { get => _name; set => Set(ref _name, value); }
    public RgbColor Color { get => _color; set => Set(ref _color, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
    public bool IsLocked { get => _isLocked; set => Set(ref _isLocked, value); }

    /// <summary>現在レイヤかどうか。<see cref="CadDocument.CurrentLayer"/> が管理する。</summary>
    public bool IsCurrent { get => _isCurrent; internal set => Set(ref _isCurrent, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override string ToString() => Name;
}
