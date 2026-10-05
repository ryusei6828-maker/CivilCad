using CivilCad.Core.Model;

namespace CivilCad.Core.Editing;

public static class EditUtil
{
    /// <summary>同じ点とみなす距離 (m)。0.1mm。</summary>
    public const double PointTolerance = 1e-4;

    /// <summary>色・線幅・タグを写す (レイヤはコンストラクタで渡す)。</summary>
    public static T CopyStyle<T>(Entity from, T to) where T : Entity
    {
        to.Color = from.Color;
        to.LineWeight = from.LineWeight;
        to.Tag = from.Tag;
        return to;
    }
}
