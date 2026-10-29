namespace CivilCad.Core.Model;

/// <summary>グループ (まとめて選ぶ図形) の扱い。</summary>
public static class Grouping
{
    /// <summary>entities に、同じグループのほかの図形 (編集できるもの) を足す。並びは図面の描画順。</summary>
    public static List<Entity> Expand(CadDocument doc, IEnumerable<Entity> entities)
    {
        var set = entities.ToHashSet();
        if (doc.EditingGroupId != null) return [.. set]; // グループ編集中は 1 つずつ選ぶ
        var groups = set.Where(e => e.GroupId != null).Select(e => e.GroupId).ToHashSet();
        if (groups.Count == 0) return [.. set];
        var result = doc.Entities.Where(e => set.Contains(e) || groups.Contains(e.GroupId) && doc.IsEditable(e)).ToList();
        result.AddRange(set.Where(e => !doc.Entities.Contains(e))); // まだ図面に無い図形 (テストなど) もそのまま
        return result;
    }

    /// <summary>
    /// 複写・貼り付けで作った図形に新しいグループを付け直す (元のグループに混ざらないように)。
    /// 同じグループだった図形どうしは、新しい同じグループになる。
    /// </summary>
    public static T RenewGroups<T>(T entities) where T : IEnumerable<Entity>
    {
        var map = new Dictionary<Guid, Guid>();
        foreach (var e in entities)
        {
            if (e.GroupId is not Guid old) continue;
            if (!map.TryGetValue(old, out var renewed)) map[old] = renewed = Guid.NewGuid();
            e.GroupId = renewed;
        }
        return entities;
    }

    /// <summary>選んだ図形に含まれるグループの数。</summary>
    public static int CountGroups(IEnumerable<Entity> entities) =>
        entities.Where(e => e.GroupId != null).Select(e => e.GroupId).Distinct().Count();
}
