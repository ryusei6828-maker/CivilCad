using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivilCad.App.Alignment;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>プロジェクトの 1 シート分 (読み書きの受け渡し用)。</summary>
public sealed record ProjectSheet(
    string Name,
    CadDocument Doc,
    AlignmentInput Alignment,
    bool HasAlignment,
    Vec2? ViewCenter = null,
    double? ViewScale = null,
    string? SourcePath = null);

/// <summary>読み込んだプロジェクト。</summary>
public sealed record ProjectData(List<ProjectSheet> Sheets, int ActiveIndex, int SkippedEntities);

/// <summary>
/// CivilCad のプロジェクトファイル (*.ccp)。開いているシートすべてを、CivilCad の図形のまま (分解せずに) 1 つのファイルに保存する。
/// 図形 (プリセット・引き出し線・寸法・画像の配置やトリミングも含む)、レイヤ、登録した座標 (点名・標高・タグ)、
/// 路線の入力 (IP 表・測点間隔・幅など)、縮尺・座標系・座標設定の向き、表示位置を持つ。
/// 中身は UTF-8 の JSON。画像は埋め込まず、画像ファイルの場所 (絶対パスと、プロジェクトからの相対パス) を書く。
/// 使っている登録プリセットの形も入れるので、プリセットを登録していない PC でも同じように表示できる。
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".ccp";
    public const string Filter = "CivilCad プロジェクト (*.ccp)|*.ccp";
    private const string Format = "CivilCadProject";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま書く
        Converters = { new JsonStringEnumConverter(), new Vec2Converter(), new ColorConverter() },
    };

    public static bool IsProject(string path) => Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    // ---- 保存 ----

    public static void Save(string path, IReadOnlyList<ProjectSheet> sheets, int activeIndex, string? appVersion = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var dto = new ProjectDto
        {
            Format = Format,
            Version = CurrentVersion,
            Application = "CivilCad " + (appVersion ?? ""),
            Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ActiveSheet = Math.Clamp(activeIndex, 0, Math.Max(0, sheets.Count - 1)),
            Sheets = [.. sheets.Select(s => ToDto(s, dir))],
        };
        // 使っている登録プリセット (組み込みの方位記号などは入れない)
        var kinds = sheets.SelectMany(s => s.Doc.Entities).OfType<SymbolEntity>().Select(e => e.Kind).ToHashSet();
        dto.Presets = [.. kinds.Where(k => SymbolLibrary.BuiltIn.All(b => b.Kind != k)).Select(SymbolLibrary.Get).Where(d => d.Kind != SymbolLibrary.BuiltIn[0].Kind)];
        if (dto.Presets.Count == 0) dto.Presets = null;

        // 書きかけで壊れないよう、一時ファイルに書いてから置き換える
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(dto, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private static SheetDto ToDto(ProjectSheet s, string projectDir)
    {
        var d = s.Doc;
        return new SheetDto
        {
            Name = s.Name,
            SourcePath = s.SourcePath,
            ViewCenter = s.ViewCenter,
            ViewScale = s.ViewScale,
            Scale = d.ScaleDenominator,
            PlaneZone = d.PlaneZone,
            SurveyRotation = d.SurveyRotation,
            SurveyPivot = d.SurveyPivot,
            UseTrueNorth = d.UseTrueNorth,
            OnlyCurrentLayerEditable = d.OnlyCurrentLayerEditable,
            CurrentLayer = d.CurrentLayer.Name,
            CurrentLineWeight = d.CurrentLineWeight,
            CurrentColor = d.CurrentColor,
            CurrentLineType = d.CurrentLineType,
            Layers = [.. d.Layers.Select(l => new LayerDto { Name = l.Name, Color = l.Color, Visible = l.IsVisible, Locked = l.IsLocked })],
            Entities = [.. d.Entities.Select(e => ToDto(e, projectDir)).OfType<EntityDto>()],
            Alignment = s.Alignment,
            HasAlignment = s.HasAlignment,
        };
    }

    private static EntityDto? ToDto(Entity e, string projectDir)
    {
        EntityDto? dto = e switch
        {
            LineEntity l => new LineDto { Start = l.Start, End = l.End },
            PolylineEntity p => new PolylineDto { Points = [.. p.Points], Closed = p.IsClosed, Filled = p.Filled },
            CircleEntity c => new CircleDto { Center = c.Center, Radius = c.Radius },
            ArcEntity a => new ArcDto { Center = a.Center, Radius = a.Radius, StartAngle = a.StartAngle, EndAngle = a.EndAngle },
            PointEntity p => new PointDto { Position = p.Position, Name = p.Name, Z = p.Z, Tags = p.Tags },
            TextEntity t => new TextDto
            {
                Position = t.Position, Text = t.Text, Height = t.Height, Rotation = t.Rotation, HAlign = t.HAlign, VAlign = t.VAlign,
                WidthFactor = t.WidthFactor, FitWidth = t.FitWidth, FontName = t.FontName, ScreenHeightPx = t.ScreenHeightPx,
            },
            HatchEntity h => new HatchDto
            {
                Loops = [.. h.Loops.Select(l => l.ToList())], Patterns = [.. h.Patterns], Solid = h.Solid,
                Opacity = h.Opacity, PatternName = h.PatternName,
            },
            ImageEntity i => new ImageDto
            {
                Path = i.Path, RelativePath = Relative(projectDir, i.Path), PixelWidth = i.PixelWidth, PixelHeight = i.PixelHeight,
                Origin = i.Origin, U = i.U, V = i.V, Page = i.Page, TransparencyPercent = i.TransparencyPercent,
                CropLeft = i.CropLeft, CropTop = i.CropTop, CropRight = i.CropRight, CropBottom = i.CropBottom, InFront = i.InFront,
            },
            SymbolEntity s => new SymbolDto { Kind = s.Kind, Position = s.Position, SizeMm = s.SizeMm, Size = s.Size, Rotation = s.Rotation, AutoNorth = s.AutoNorth },
            LeaderEntity l => new LeaderDto { Points = [.. l.Points], IsFlag = l.IsFlag, Text = l.Text, TextHeight = l.TextHeight, TipSize = l.TipSize, Tip = l.Tip },
            DimensionEntity d => new DimensionDto
            {
                P1 = d.P1, P2 = d.P2, LinePoint = d.LinePoint, Angle = d.Angle, TextHeight = d.TextHeight, TerminatorSize = d.TerminatorSize,
                Terminator = d.Terminator, Unit = d.Unit, Decimals = d.Decimals, TextOverride = d.TextOverride,
            },
            _ => null,
        };
        if (dto == null) return null;
        dto.Layer = e.Layer.Name;
        dto.Color = e.Color;
        dto.Tag = e.Tag;
        dto.LineWeight = e.LineWeight;
        dto.LineType = e.LineType == LineType.Continuous ? null : e.LineType;
        return dto;
    }

    private static string? Relative(string dir, string path)
    {
        try
        {
            return Path.IsPathRooted(path) ? Path.GetRelativePath(dir, path) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // ---- 読み込み ----

    public static ProjectData Load(string path)
    {
        ProjectDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProjectDto>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"CivilCad のプロジェクトファイルとして読めませんでした ({ex.Message})。", ex);
        }
        if (dto == null || dto.Format != Format) throw new InvalidDataException("CivilCad のプロジェクトファイルではありません。");
        if (dto.Version > CurrentVersion)
            throw new InvalidDataException($"新しい版の CivilCad で保存されたプロジェクトです (形式 {dto.Version})。CivilCad を更新してください。");

        // 入っている登録プリセットのうち、この PC に無いものは表示用に登録する
        foreach (var def in dto.Presets ?? []) SymbolLibrary.RegisterForDisplay(def);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var skipped = 0;
        var sheets = new List<ProjectSheet>();
        foreach (var s in dto.Sheets ?? [])
        {
            var doc = new CadDocument();
            skipped += Fill(doc, s, dir);
            sheets.Add(new ProjectSheet(s.Name ?? $"シート{sheets.Count + 1}", doc, s.Alignment ?? AlignmentInput.Sample(), s.HasAlignment,
                s.ViewCenter, s.ViewScale, s.SourcePath));
        }
        if (sheets.Count == 0) throw new InvalidDataException("プロジェクトにシートがありません。");
        return new ProjectData(sheets, Math.Clamp(dto.ActiveSheet, 0, sheets.Count - 1), skipped);
    }

    /// <summary>シートの中身を図面に入れる。読めなかった図形の数を返す。</summary>
    private static int Fill(CadDocument doc, SheetDto s, string projectDir)
    {
        doc.Clear();
        if (s.Scale > 0) doc.ScaleDenominator = s.Scale;
        doc.PlaneZone = s.PlaneZone;
        doc.SetSurveyFrame(s.SurveyRotation, s.SurveyPivot);
        doc.UseTrueNorth = s.UseTrueNorth;
        foreach (var l in s.Layers ?? [])
        {
            if (string.IsNullOrEmpty(l.Name)) continue;
            var layer = doc.GetOrCreateLayer(l.Name, l.Color);
            layer.Color = l.Color;
            layer.IsVisible = l.Visible;
            layer.IsLocked = l.Locked;
        }
        Layer LayerOf(string? name) => doc.FindLayer(name ?? "0") ?? doc.GetOrCreateLayer(name ?? "0", RgbColor.White);

        var skipped = 0;
        foreach (var e in s.Entities ?? [])
        {
            Entity? made;
            try
            {
                made = FromDto(e, LayerOf(e.Layer), projectDir);
            }
            catch (Exception ex) when (ex is ArgumentException or NullReferenceException or InvalidOperationException)
            {
                made = null;
            }
            if (made == null)
            {
                skipped++;
                continue;
            }
            made.Color = e.Color;
            made.Tag = e.Tag;
            made.LineWeight = e.LineWeight;
            made.LineType = e.LineType ?? LineType.Continuous;
            doc.Entities.Add(made);
        }

        // 現在レイヤは最後に (現在レイヤにすると表示・ロック解除になるので、レイヤの状態を入れた後で)
        if (s.CurrentLayer != null && doc.FindLayer(s.CurrentLayer) is { } current) doc.CurrentLayer = current;
        doc.OnlyCurrentLayerEditable = s.OnlyCurrentLayerEditable;
        doc.CurrentLineWeight = s.CurrentLineWeight;
        doc.CurrentColor = s.CurrentColor;
        doc.CurrentLineType = s.CurrentLineType;
        doc.History.Clear();
        doc.NotifyChanged();
        return skipped;
    }

    private static Entity? FromDto(EntityDto e, Layer layer, string projectDir) => e switch
    {
        LineDto l => new LineEntity(layer, l.Start, l.End),
        PolylineDto p when p.Points is { Count: > 0 } => new PolylineEntity(layer, p.Points, p.Closed) { Filled = p.Filled },
        CircleDto c => new CircleEntity(layer, c.Center, c.Radius),
        ArcDto a => new ArcEntity(layer, a.Center, a.Radius, a.StartAngle, a.EndAngle),
        PointDto p => new PointEntity(layer, p.Position) { Name = p.Name ?? "", Z = p.Z, Tags = p.Tags ?? "" },
        TextDto t => new TextEntity(layer, t.Position, t.Text ?? "", t.Height, t.Rotation)
        {
            HAlign = t.HAlign, VAlign = t.VAlign, WidthFactor = t.WidthFactor is > 0 ? t.WidthFactor : 1, FitWidth = t.FitWidth,
            FontName = t.FontName, ScreenHeightPx = t.ScreenHeightPx,
        },
        HatchDto h when h.Loops is { Count: > 0 } => new HatchEntity(layer, h.Loops, h.Patterns ?? [], h.Solid) { Opacity = h.Opacity, PatternName = h.PatternName },
        ImageDto i when i.Path != null => new ImageEntity(layer, ResolveImage(projectDir, i), i.PixelWidth, i.PixelHeight, i.Origin, i.U, i.V)
        {
            Page = i.Page, TransparencyPercent = i.TransparencyPercent,
            CropLeft = i.CropLeft, CropTop = i.CropTop, CropRight = i.CropRight, CropBottom = i.CropBottom, InFront = i.InFront,
        },
        SymbolDto s when s.Kind != null => new SymbolEntity(layer, s.Kind, s.Position, s.SizeMm, s.Size, s.Rotation) { AutoNorth = s.AutoNorth },
        LeaderDto l when l.Points is { Count: >= 2 } => new LeaderEntity(layer, l.Points, l.IsFlag, l.Text ?? "", l.TextHeight, l.TipSize, l.Tip),
        DimensionDto d => new DimensionEntity(layer, d.P1, d.P2, d.LinePoint, d.Angle, d.TextHeight, d.TerminatorSize)
        {
            Terminator = d.Terminator, Unit = d.Unit, Decimals = d.Decimals, TextOverride = d.TextOverride,
        },
        _ => null,
    };

    /// <summary>画像の場所: 保存したときの場所に無ければ、プロジェクトからの相対の場所 (フォルダごと移したとき)。</summary>
    private static string ResolveImage(string projectDir, ImageDto i)
    {
        if (File.Exists(i.Path) || i.RelativePath == null) return i.Path!;
        var rel = Path.GetFullPath(Path.Combine(projectDir, i.RelativePath));
        return File.Exists(rel) ? rel : i.Path!;
    }

    // ---- JSON の形 ----

    private sealed class ProjectDto
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public string? Application { get; set; }
        public string? Saved { get; set; }
        public int ActiveSheet { get; set; }
        public List<SheetDto>? Sheets { get; set; }
        public List<SymbolDefinition>? Presets { get; set; }
    }

    private sealed class SheetDto
    {
        public string? Name { get; set; }
        public string? SourcePath { get; set; }
        public Vec2? ViewCenter { get; set; }
        public double? ViewScale { get; set; }
        public double Scale { get; set; } = 500;
        public int? PlaneZone { get; set; }
        public double SurveyRotation { get; set; }
        public Vec2 SurveyPivot { get; set; }
        public bool UseTrueNorth { get; set; } = true;
        public bool OnlyCurrentLayerEditable { get; set; }
        public string? CurrentLayer { get; set; }
        public double? CurrentLineWeight { get; set; }
        public RgbColor? CurrentColor { get; set; }
        public LineType CurrentLineType { get; set; }
        public List<LayerDto>? Layers { get; set; }
        public List<EntityDto>? Entities { get; set; }
        public AlignmentInput? Alignment { get; set; }
        public bool HasAlignment { get; set; }
    }

    private sealed class LayerDto
    {
        public string? Name { get; set; }
        public RgbColor Color { get; set; } = RgbColor.White;
        public bool Visible { get; set; } = true;
        public bool Locked { get; set; }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(LineDto), "line")]
    [JsonDerivedType(typeof(PolylineDto), "polyline")]
    [JsonDerivedType(typeof(CircleDto), "circle")]
    [JsonDerivedType(typeof(ArcDto), "arc")]
    [JsonDerivedType(typeof(PointDto), "point")]
    [JsonDerivedType(typeof(TextDto), "text")]
    [JsonDerivedType(typeof(HatchDto), "hatch")]
    [JsonDerivedType(typeof(ImageDto), "image")]
    [JsonDerivedType(typeof(SymbolDto), "symbol")]
    [JsonDerivedType(typeof(LeaderDto), "leader")]
    [JsonDerivedType(typeof(DimensionDto), "dimension")]
    private abstract class EntityDto
    {
        public string? Layer { get; set; }
        public RgbColor? Color { get; set; }
        public string? Tag { get; set; }
        public double? LineWeight { get; set; }
        public LineType? LineType { get; set; }
    }

    private sealed class LineDto : EntityDto
    {
        public Vec2 Start { get; set; }
        public Vec2 End { get; set; }
    }

    private sealed class PolylineDto : EntityDto
    {
        public List<Vec2>? Points { get; set; }
        public bool Closed { get; set; }
        public bool Filled { get; set; }
    }

    private sealed class CircleDto : EntityDto
    {
        public Vec2 Center { get; set; }
        public double Radius { get; set; }
    }

    private sealed class ArcDto : EntityDto
    {
        public Vec2 Center { get; set; }
        public double Radius { get; set; }
        public double StartAngle { get; set; }
        public double EndAngle { get; set; }
    }

    private sealed class PointDto : EntityDto
    {
        public Vec2 Position { get; set; }
        public string? Name { get; set; }
        public double? Z { get; set; }
        public string? Tags { get; set; }
    }

    private sealed class TextDto : EntityDto
    {
        public Vec2 Position { get; set; }
        public string? Text { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public TextHAlign HAlign { get; set; }
        public TextVAlign VAlign { get; set; }
        public double WidthFactor { get; set; } = 1;
        public double? FitWidth { get; set; }
        public string? FontName { get; set; }
        public double? ScreenHeightPx { get; set; }
    }

    private sealed class HatchDto : EntityDto
    {
        public List<List<Vec2>>? Loops { get; set; }
        public List<HatchPattern>? Patterns { get; set; }
        public bool Solid { get; set; }
        public double Opacity { get; set; } = 1;
        public string? PatternName { get; set; }
    }

    private sealed class ImageDto : EntityDto
    {
        public string? Path { get; set; }
        public string? RelativePath { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public Vec2 Origin { get; set; }
        public Vec2 U { get; set; }
        public Vec2 V { get; set; }
        public int Page { get; set; }
        public double TransparencyPercent { get; set; }
        public double CropLeft { get; set; }
        public double CropTop { get; set; }
        public double CropRight { get; set; } = 1;
        public double CropBottom { get; set; } = 1;
        public bool InFront { get; set; }
    }

    private sealed class SymbolDto : EntityDto
    {
        public string? Kind { get; set; }
        public Vec2 Position { get; set; }
        public double SizeMm { get; set; }
        public double Size { get; set; }
        public double Rotation { get; set; }
        public bool AutoNorth { get; set; } = true;
    }

    private sealed class LeaderDto : EntityDto
    {
        public List<Vec2>? Points { get; set; }
        public bool IsFlag { get; set; }
        public string? Text { get; set; }
        public double TextHeight { get; set; }
        public double TipSize { get; set; }
        public LeaderTip Tip { get; set; }
    }

    private sealed class DimensionDto : EntityDto
    {
        public Vec2 P1 { get; set; }
        public Vec2 P2 { get; set; }
        public Vec2 LinePoint { get; set; }
        public double? Angle { get; set; }
        public double TextHeight { get; set; }
        public double TerminatorSize { get; set; }
        public DimTerminator Terminator { get; set; }
        public DimUnit Unit { get; set; }
        public int Decimals { get; set; } = 3;
        public string? TextOverride { get; set; }
    }

    /// <summary>座標は [x, y] で書く。</summary>
    private sealed class Vec2Converter : JsonConverter<Vec2>
    {
        public override Vec2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var v = JsonSerializer.Deserialize<double[]>(ref reader) ?? [];
            if (v.Length < 2) throw new JsonException("座標は [x, y] で書いてください");
            return new Vec2(v[0], v[1]);
        }

        public override void Write(Utf8JsonWriter writer, Vec2 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }

    /// <summary>色は "#RRGGBB" で書く。</summary>
    private sealed class ColorConverter : JsonConverter<RgbColor>
    {
        public override RgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString() ?? "";
            if (s.Length != 7 || s[0] != '#' || !int.TryParse(s[1..], System.Globalization.NumberStyles.HexNumber, null, out var v))
                throw new JsonException($"色は #RRGGBB で書いてください: {s}");
            return new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        public override void Write(Utf8JsonWriter writer, RgbColor value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}");
    }
}
