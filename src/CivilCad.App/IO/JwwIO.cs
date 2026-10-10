using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>
/// Jw_cad (.jww) の読み書き。形式は作者が公開している「Jw_cad のデータ形式」(jwdatafmt.txt) による。
/// ファイルは MFC の CArchive で、ヘッダー・図形データのリスト・ブロック定義のリスト (Ver.7 以降は画像) が続く。
/// <para>
/// JWW の座標は用紙上の mm (図寸) で原点は用紙の中心。実寸はレイヤグループの縮尺を掛けたもので、
/// 図面 (m) = 図寸 mm × 縮尺 / 1000 として取り込む。レイヤは「グループ番号-レイヤ番号 名前」(16 進) にする。
/// </para>
/// </summary>
public static partial class JwwIO
{
    /// <summary>保存するデータの版 (Jw_cad 7.02 以降の 700)。</summary>
    public const int SaveVersion = 700;

    public sealed record LoadResult(int Entities, int Skipped, IReadOnlyDictionary<string, int> SkippedKinds, double ScaleDenominator, Box2? Sheet, int Version);

    /// <summary>図面サイズの番号 → 用紙 (横置き, mm)。</summary>
    private static (double W, double H)? PaperOf(uint code) => code switch
    {
        0 => (1189, 841), 1 => (841, 594), 2 => (594, 420), 3 => (420, 297), 4 => (297, 210),
        8 => (1682, 1189), 9 => (2378, 1682), 10 => (3364, 2378), 11 => (4756, 3364),
        12 => (10000, 7071), 13 => (50000, 35355), 14 => (100000, 70711),
        _ => null,
    };

    /// <summary>線色 1〜9 の既定の色 (画面が暗い背景の CivilCad 向け)。2 (黒) は白で表示する。</summary>
    private static readonly RgbColor[] PenColors =
    [
        RgbColor.White,          // 0 (背景色。使われない)
        new(0, 192, 255),        // 1 水色
        RgbColor.White,          // 2 黒 (暗い背景では白)
        new(0, 200, 0),          // 3 緑
        new(220, 170, 0),        // 4 黄
        new(230, 0, 230),        // 5 ピンク
        new(0, 96, 255),         // 6 青
        new(0, 160, 160),        // 7 シアン
        new(255, 40, 40),        // 8 赤
        new(160, 160, 160),      // 9 グレー
    ];

    // ---- 読み込み ----

    public static LoadResult Load(CadDocument doc, string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 8) != "JwwData.")
            throw new InvalidDataException("Jw_cad の図面ファイル (JWW) ではありません。");

        // ブロック定義の時刻 (CTime) は Jw_cad のビルドによって 4 バイトか 8 バイト。読み切れる方を使う
        JwwData? best = null;
        foreach (var timeSize in new[] { 8, 4 })
        {
            var data = JwwData.Read(bytes, timeSize);
            if (best == null || data.Complete && !best.Complete || data.Complete == best.Complete && data.Figures.Count > best.Figures.Count)
                best = data;
            if (data.Complete) break;
        }
        return new Importer(doc, best!).Run();
    }

    /// <summary>読み込んだ図形 (JWW の値のまま)。</summary>
    /// <summary>
    /// 線種 → Jw_cad の線種番号 (1 実線, 2〜4 点線1〜3, 5・6 一点鎖1・2, 7・8 二点鎖1・2)。
    /// Jw_cad の「点線」は短い線の繰り返しなので破線は点線1、点線は点線3 にする。
    /// </summary>
    public static int PenStyle(LineType t) => t switch
    {
        LineType.Dashed => 2,
        LineType.Dotted => 4,
        LineType.DashDot => 5,
        LineType.DashDotDot => 7,
        _ => 1,
    };

    public static LineType LineTypeOf(int style) => style switch
    {
        2 or 3 => LineType.Dashed,
        4 => LineType.Dotted,
        5 or 6 => LineType.DashDot,
        7 or 8 => LineType.DashDotDot,
        _ => LineType.Continuous,
    };

    private abstract record Figure(uint Group, byte Style, ushort Color, ushort Width, ushort Layer, ushort GLayer, ushort Flag);

    private sealed record Sen(uint Group, byte Style, ushort Color, ushort Width, ushort Layer, ushort GLayer, ushort Flag, Vec2 A, Vec2 B)
        : Figure(Group, Style, Color, Width, Layer, GLayer, Flag);

    private sealed record Enko(uint Group, byte Style, ushort Color, ushort Width, ushort Layer, ushort GLayer, ushort Flag,
        Vec2 Center, double R, double Start, double Arc, double Tilt, double Flat, bool Full)
        : Figure(Group, Style, Color, Width, Layer, GLayer, Flag);

    private sealed record Ten(uint Group, byte Style, ushort Color, ushort Width, ushort Layer, ushort GLayer, ushort Flag, Vec2 P, bool Kari)
        : Figure(Group, Style, Color, Width, Layer, GLayer, Flag);

    private sealed record Moji(uint Group, byte Style, ushort Color, ushort Width, ushort Layer, ushort GLayer, ushort Flag,
        Vec2 Start, Vec2 End, uint Kind, double SizeX, double SizeY, double Spacing, double AngleDeg, string Font, string Text)
        : Figure(Group, Style, Color, Width, Layer, GLayer, Flag);

    private sealed record Solid(uint Group, byte Style, ushort Color, ushort Width, ushort Layer, ushort GLayer, ushort Flag,
        Vec2 P1, Vec2 P4, Vec2 P2, Vec2 P3, uint? Rgb)
        : Figure(Group, Style, Color, Width, Layer, GLayer, Flag);

    private sealed record Sunpou(uint Group, byte Style, ushort Color, ushort Width, ushort Layer, ushort GLayer, ushort Flag, Sen Line, Moji Value, List<Figure> Extras)
        : Figure(Group, Style, Color, Width, Layer, GLayer, Flag);

    private sealed record BlockRef(uint Group, byte Style, ushort Color, ushort Width, ushort Layer, ushort GLayer, ushort Flag,
        Vec2 P, double Sx, double Sy, double Rot, uint Number)
        : Figure(Group, Style, Color, Width, Layer, GLayer, Flag);

    private sealed record BlockDef(uint Number, string Name, List<Figure> Members);

    private sealed class Header
    {
        public uint Version;
        public string Memo = "";
        public uint Paper;
        public readonly uint[] GroupState = new uint[16];
        public readonly double[] GroupScale = new double[16];
        public readonly uint[,] LayerState = new uint[16, 16];
        public readonly string[,] LayerNames = new string[16, 16];
        public readonly string[] GroupNames = new string[16];
        public int MaxDrawWid;
        public readonly uint[] ScreenColors = new uint[10];
        public readonly uint[] SxfScreenColors = new uint[257];
    }

    /// <summary>CArchive の読み手。足りないところを読むと EndOfStreamException。</summary>
    private sealed class Archive(byte[] b)
    {
        public int Pos;
        private readonly List<string?> _classes = [null]; // クラスとオブジェクトが同じ番号を使う (0 は null)
        private static readonly Encoding Sjis = Encoding.GetEncoding(932);

        private void Need(int n)
        {
            if (Pos + n > b.Length) throw new EndOfStreamException();
        }

        public bool AtEnd => Pos >= b.Length;
        public byte U8() { Need(1); return b[Pos++]; }
        public ushort U16() { Need(2); var v = BitConverter.ToUInt16(b, Pos); Pos += 2; return v; }
        public uint U32() { Need(4); var v = BitConverter.ToUInt32(b, Pos); Pos += 4; return v; }
        public int I32() => unchecked((int)U32());
        public double F64() { Need(8); var v = BitConverter.ToDouble(b, Pos); Pos += 8; return v; }
        public Vec2 P() => new(F64(), F64());
        public void Skip(int n) { Need(n); Pos += n; }

        /// <summary>CString: 長さ (BYTE、0xFF の後に WORD、0xFFFF の後に DWORD)。0xFFFE は UTF-16 の印。</summary>
        public string Str()
        {
            var unicode = false;
            uint len = U8();
            if (len == 0xFF)
            {
                len = U16();
                if (len == 0xFFFE)
                {
                    unicode = true;
                    len = U8();
                    if (len == 0xFF) len = U16();
                }
                if (len == 0xFFFF) len = U32();
            }
            if (len > 100_000_000) throw new EndOfStreamException();
            var n = (int)len * (unicode ? 2 : 1);
            Need(n);
            var s = unicode ? Encoding.Unicode.GetString(b, Pos, n) : Sjis.GetString(b, Pos, n);
            Pos += n;
            return s;
        }

        /// <summary>CObList の要素数 (WORD、0xFFFF の後に DWORD)。</summary>
        public uint Count()
        {
            var w = U16();
            return w != 0xFFFF ? w : U32();
        }

        /// <summary>要素の前のタグを読み、クラス名を返す (null オブジェクト・既出のオブジェクトは null)。</summary>
        public string? ObjectTag()
        {
            var w = U16();
            if (w == 0) return null;
            if (w == 0xFFFF)
            {
                U16(); // スキーマ
                var len = U16();
                Need(len);
                var name = Encoding.ASCII.GetString(b, Pos, len);
                Pos += len;
                _classes.Add(name);
                _classes.Add(null); // オブジェクト
                return name;
            }
            uint tag = w == 0x7FFF ? U32() : (w & 0x8000) != 0 ? 0x80000000u | (uint)(w & 0x7FFF) : w;
            if ((tag & 0x80000000) == 0)
            {
                if (tag >= _classes.Count) throw new InvalidDataException("JWW の参照が壊れています。");
                return null; // 既に読んだオブジェクトの参照 (データは続かない)
            }
            var index = (int)(tag & 0x7FFFFFFF);
            if (index >= _classes.Count || _classes[index] is not { } cls) throw new InvalidDataException("JWW の図形のクラスが分かりません。");
            _classes.Add(null);
            return cls;
        }
    }

    private sealed class JwwData
    {
        public Header H = new();
        public List<Figure> Figures = [];
        public Dictionary<uint, BlockDef> Blocks = [];
        public Dictionary<string, int> Unknown = [];
        /// <summary>最後まで読めたか (途中で壊れていても、読めたところまでは使う)。</summary>
        public bool Complete;

        public static JwwData Read(byte[] bytes, int timeSize)
        {
            var d = new JwwData();
            var a = new Archive(bytes) { Pos = 8 };
            try
            {
                ReadHeader(a, d.H);
            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException("JWW のヘッダーを読めませんでした (ファイルが壊れているか、未対応の版です)。");
            }
            try
            {
                var r = new FigureReader(a, d, timeSize);
                var n = a.Count();
                for (var i = 0; i < n; i++)
                    if (r.ReadObject() is { } f) d.Figures.Add(f);
                    else if (r.Stopped) return d;
                var defs = a.Count();
                for (var i = 0; i < defs; i++)
                {
                    r.ReadObject();
                    if (r.Stopped) return d;
                }
                // Ver.7.00 以降は画像が続く (無いファイルもある)
                if (d.H.Version >= 700 && !a.AtEnd)
                {
                    var images = a.U32();
                    for (var i = 0; i < images; i++)
                    {
                        a.Str();
                        a.Skip((int)a.U32());
                    }
                }
                // ちょうど最後まで読めたときだけ正しく読めたとみなす (時刻の大きさの見分けにも使う)
                d.Complete = a.AtEnd;
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
            {
                d.Complete = false;
            }
            return d;
        }
    }

    private sealed class FigureReader(Archive a, JwwData d, int timeSize)
    {
        public bool Stopped;
        private uint V => d.H.Version;

        public Figure? ReadObject()
        {
            var cls = a.ObjectTag();
            switch (cls)
            {
                case null: return null;
                case "CDataSen": return ReadSen();
                case "CDataEnko": return ReadEnko();
                case "CDataTen": return ReadTen();
                case "CDataMoji": return ReadMoji();
                case "CDataSolid": return ReadSolid();
                case "CDataSunpou": return ReadSunpou();
                case "CDataBlock": return ReadBlock();
                case "CDataList":
                    ReadList();
                    return null;
                default:
                    // 大きさの分からないクラスがあると、その先は読めない
                    d.Unknown[cls] = d.Unknown.GetValueOrDefault(cls) + 1;
                    Stopped = true;
                    return null;
            }
        }

        private (uint G, byte S, ushort C, ushort W, ushort L, ushort GL, ushort F) Base()
        {
            var g = a.U32();
            var s = a.U8();
            var c = a.U16();
            ushort w = V >= 351 ? a.U16() : (ushort)0;
            return (g, s, c, w, a.U16(), a.U16(), a.U16());
        }

        private Sen ReadSen()
        {
            var b = Base();
            return new Sen(b.G, b.S, b.C, b.W, b.L, b.GL, b.F, a.P(), a.P());
        }

        private Enko ReadEnko()
        {
            var b = Base();
            return new Enko(b.G, b.S, b.C, b.W, b.L, b.GL, b.F, a.P(), a.F64(), a.F64(), a.F64(), a.F64(), a.F64(), a.U32() != 0);
        }

        private Ten ReadTen()
        {
            var b = Base();
            var p = a.P();
            var kari = a.U32() != 0;
            if (b.S == 100)
            {
                a.U32();
                a.F64();
                a.F64();
            }
            return new Ten(b.G, b.S, b.C, b.W, b.L, b.GL, b.F, p, kari);
        }

        private Moji ReadMoji()
        {
            var b = Base();
            return new Moji(b.G, b.S, b.C, b.W, b.L, b.GL, b.F, a.P(), a.P(), a.U32(), a.F64(), a.F64(), a.F64(), a.F64(), a.Str(), a.Str());
        }

        private Solid ReadSolid()
        {
            var b = Base();
            var (p1, p4, p2, p3) = (a.P(), a.P(), a.P(), a.P());
            uint? rgb = b.C == 10 ? a.U32() : null;
            return new Solid(b.G, b.S, b.C, b.W, b.L, b.GL, b.F, p1, p4, p2, p3, rgb);
        }

        private Sunpou ReadSunpou()
        {
            var b = Base();
            var line = ReadSen();
            var value = ReadMoji();
            var extras = new List<Figure>();
            if (V >= 420)
            {
                a.U16();
                extras.Add(ReadSen());
                extras.Add(ReadSen());
                for (var i = 0; i < 4; i++) extras.Add(ReadTen());
            }
            return new Sunpou(b.G, b.S, b.C, b.W, b.L, b.GL, b.F, line, value, extras);
        }

        private BlockRef ReadBlock()
        {
            var b = Base();
            return new BlockRef(b.G, b.S, b.C, b.W, b.L, b.GL, b.F, a.P(), a.F64(), a.F64(), a.F64(), a.U32());
        }

        private void ReadList()
        {
            Base();
            var number = a.U32();
            a.U32();
            a.Skip(timeSize);
            var name = a.Str();
            var members = new List<Figure>();
            var n = a.Count();
            for (var i = 0; i < n && !Stopped; i++)
                if (ReadObject() is { } f) members.Add(f);
            d.Blocks[number] = new BlockDef(number, name, members);
        }
    }

    /// <summary>ヘッダー (データ形式の順。版で増えた項目はその版以降のファイルだけ)。</summary>
    private static void ReadHeader(Archive a, Header h)
    {
        var v = h.Version = a.U32();
        h.Memo = a.Str();
        h.Paper = a.U32();
        a.U32(); // 書込レイヤグループ
        for (var g = 0; g < 16; g++)
        {
            h.GroupState[g] = a.U32();
            a.U32();
            h.GroupScale[g] = a.F64();
            a.U32();
            for (var l = 0; l < 16; l++)
            {
                h.LayerState[g, l] = a.U32();
                a.U32();
            }
        }
        for (var i = 0; i < 14 + 5 + 1; i++) a.U32(); // ダミー・寸法設定・ダミー
        h.MaxDrawWid = a.I32();
        a.F64(); a.F64(); a.F64(); // プリンタ出力範囲の原点・倍率
        a.U32(); a.U32();          // プリンタ 90°回転・目盛設定モード
        for (var i = 0; i < 5; i++) a.F64(); // 目盛
        for (var g = 0; g < 16; g++)
            for (var l = 0; l < 16; l++)
                h.LayerNames[g, l] = a.Str();
        for (var g = 0; g < 16; g++) h.GroupNames[g] = a.Str();
        a.F64(); a.F64(); a.U32(); a.F64(); // 日影
        if (v >= 300) { a.F64(); a.F64(); } // 天空図
        a.U32(); // 2.5D の計算単位
        for (var i = 0; i < 6; i++) a.F64(); // 画面倍率・範囲記憶
        if (v >= 300)
        {
            for (var n = 0; n < 8; n++) { a.F64(); a.F64(); a.F64(); a.U32(); }
            a.F64(); a.F64(); a.F64(); a.U32(); a.F64(); a.F64(); a.F64(); a.U32(); // 文字の描画状態など
        }
        else
        {
            for (var n = 0; n < 4; n++) { a.F64(); a.F64(); a.F64(); }
        }
        for (var i = 0; i < 11; i++) a.F64(); // 複線間隔・両側複線の留線出
        for (var n = 0; n < 10; n++) { h.ScreenColors[n] = a.U32(); a.U32(); }
        for (var n = 0; n < 10; n++) { a.U32(); a.U32(); a.F64(); }
        for (var n = 0; n < 8; n++) for (var k = 0; k < 4; k++) a.U32();
        for (var n = 0; n < 5; n++) for (var k = 0; k < 5; k++) a.U32();
        for (var n = 0; n < 4; n++) for (var k = 0; k < 4; k++) a.U32();
        for (var i = 0; i < 11; i++) a.U32(); // 実点・描画順・印刷の設定
        if (v >= 223)
        {
            for (var i = 0; i < 5; i++) a.U32();
            for (var i = 0; i < 5; i++) a.F64();
        }
        if (v >= 225) for (var i = 0; i < 4; i++) a.F64();
        if (v >= 230) { a.U32(); a.U32(); }
        if (v >= 420)
        {
            for (var n = 0; n <= 256; n++) { h.SxfScreenColors[n] = a.U32(); a.U32(); }
            for (var n = 0; n <= 256; n++) { a.Str(); a.U32(); a.U32(); a.F64(); }
            for (var n = 0; n <= 32; n++) for (var k = 0; k < 4; k++) a.U32();
            for (var n = 0; n <= 32; n++)
            {
                a.Str();
                a.U32();
                for (var j = 0; j < 10; j++) a.F64();
            }
        }
        for (var n = 0; n < 10; n++) { a.F64(); a.F64(); a.F64(); a.U32(); } // 文字種 1〜10
        a.F64(); a.F64(); a.F64(); a.U32(); a.U32(); // 書込み文字
        a.F64(); a.F64(); // 文字位置整理
        a.U32();
        for (var i = 0; i < 6; i++) a.F64(); // 文字基準点のずれ
    }

    /// <summary>JWW の図形を図面に取り込む。</summary>
    private sealed class Importer(CadDocument doc, JwwData d)
    {
        // CivilCad で保存したファイルは、用紙の中心の図面座標をファイルメモに入れてある
        private readonly Vec2 _origin = OriginFromMemo(d.H.Memo);
        private readonly Layer?[,] _layers = new Layer?[16, 16];
        private readonly List<Entity> _out = [];
        private readonly Dictionary<string, int> _skipped = [];
        private int _skippedCount;

        public LoadResult Run()
        {
            var h = d.H;
            doc.Clear();
            var scale = MainScale();
            doc.ScaleDenominator = scale;
            foreach (var f in d.Figures) Emit(f, null, 0, null);
            foreach (var (cls, n) in d.Unknown) Skip($"未対応の図形 {cls} (以降は読めません)", n);
            if (!d.Complete) Skip("ファイルの途中で壊れている部分", 1);
            doc.Entities.AddRange(_out);
            doc.NotifyChanged();
            // 用紙: 原点が用紙の中心
            Box2? sheet = null;
            if (PaperOf(h.Paper) is var (w, hh))
            {
                var k = scale / 1000;
                sheet = new Box2(new Vec2(-w / 2, -hh / 2) * k + _origin, new Vec2(w / 2, hh / 2) * k + _origin);
            }
            return new LoadResult(_out.Count, _skippedCount, _skipped, scale, sheet, (int)h.Version);
        }

        /// <summary>図面の縮尺 = 図形の数が最も多いレイヤグループの縮尺。</summary>
        private double MainScale()
        {
            var best = d.Figures.GroupBy(f => f.GLayer & 15).OrderByDescending(g => g.Count()).FirstOrDefault();
            var s = best != null ? d.H.GroupScale[best.Key] : d.H.GroupScale[0];
            return s > 0 && double.IsFinite(s) ? s : 1;
        }

        private double ScaleOf(int glayer)
        {
            var s = d.H.GroupScale[glayer & 15];
            return (s > 0 && double.IsFinite(s) ? s : 1) / 1000; // 図寸 1mm = s/1000 [m]
        }

        private Layer LayerOf(Figure f)
        {
            int g = f.GLayer & 15, l = f.Layer & 15;
            if (_layers[g, l] is { } layer) return layer;
            var name = d.H.LayerNames[g, l];
            // 名前の無い 0-0 は既定のレイヤ「0」にする
            var full = g == 0 && l == 0 && string.IsNullOrWhiteSpace(name) ? "0"
                : $"{g:X}-{l:X}" + (string.IsNullOrWhiteSpace(name) ? "" : " " + name.Trim());
            layer = doc.GetOrCreateLayer(full, RgbColor.White);
            layer.IsVisible = d.H.GroupState[g] != 0 && d.H.LayerState[g, l] != 0;
            return _layers[g, l] = layer;
        }

        private void Skip(string kind, int n = 1)
        {
            _skipped[kind] = _skipped.GetValueOrDefault(kind) + n;
            _skippedCount += n;
        }

        /// <param name="m">ブロックの中の図形の変換 (図寸 mm → 図寸 mm)。null は用紙そのまま。</param>
        /// <param name="owner">ブロック参照 (中の図形のレイヤグループの縮尺には参照側を使う)。</param>
        private void Emit(Figure f, Func<Vec2, Vec2>? m, int depth, Figure? owner)
        {
            var k = ScaleOf((owner ?? f).GLayer);
            Vec2 W(Vec2 p) => (m != null ? m(p) : p) * k + _origin;
            var lengthScale = m != null ? (W(new Vec2(1, 0)) - W(Vec2.Zero)).Length / k : 1;
            var layer = LayerOf(f);
            switch (f)
            {
                case Sen s:
                    Add(f, new LineEntity(layer, W(s.A), W(s.B)));
                    break;
                case Enko e:
                    EmitEnko(e, layer, W, k * lengthScale, m);
                    break;
                case Ten t:
                    if (t.Kari) break; // 仮点は印刷されない
                    Add(f, new PointEntity(layer, W(t.P)));
                    break;
                case Moji t:
                    EmitText(t, layer, W, k * lengthScale, m);
                    break;
                case Solid s:
                    EmitSolid(s, layer, W, k * lengthScale);
                    break;
                case Sunpou sp:
                    // 寸法線と寸法値 (Ver.4.20 以降の補助線も)
                    Emit(sp.Line, m, depth, owner);
                    Emit(sp.Value, m, depth, owner);
                    foreach (var x in sp.Extras.OfType<Sen>()) if ((x.A - x.B).LengthSquared > 0) Emit(x, m, depth, owner);
                    break;
                case BlockRef br:
                    if (depth >= 16 || !d.Blocks.TryGetValue(br.Number, out var def))
                    {
                        Skip("ブロック (定義なし)");
                        break;
                    }
                    var (cos, sin) = (Math.Cos(br.Rot), Math.Sin(br.Rot));
                    Vec2 Local(Vec2 p)
                    {
                        var q = new Vec2(p.X * br.Sx, p.Y * br.Sy);
                        var r = new Vec2(q.X * cos - q.Y * sin, q.X * sin + q.Y * cos) + br.P;
                        return m != null ? m(r) : r;
                    }
                    foreach (var child in def.Members) Emit(child, Local, depth + 1, owner ?? br);
                    break;
            }
        }

        private void EmitEnko(Enko e, Layer layer, Func<Vec2, Vec2> W, double k, Func<Vec2, Vec2>? m)
        {
            var simple = Math.Abs(e.Flat - 1) < 1e-9 && m == null;
            if (simple)
            {
                if (e.Full) Add(e, new CircleEntity(layer, W(e.Center), e.R * k));
                else
                {
                    var (s, sweep) = e.Arc >= 0 ? (e.Start, e.Arc) : (e.Start + e.Arc, -e.Arc);
                    Add(e, new ArcEntity(layer, W(e.Center), e.R * k, s, s + sweep));
                }
                return;
            }
            if (Math.Abs(e.Flat - 1) < 1e-9 && m != null)
            {
                // ブロックの中の円・円弧: 中心と、円周上の点を変換して半径と角度を求める
                var c = W(e.Center);
                var r = (W(e.Center + new Vec2(e.R, 0)) - c).Length;
                if (e.Full) { Add(e, new CircleEntity(layer, c, r)); return; }
                var (s, sweep) = e.Arc >= 0 ? (e.Start, e.Arc) : (e.Start + e.Arc, -e.Arc);
                var a0 = (W(e.Center + Vec2.FromAngle(s) * e.R) - c).Angle;
                var a1 = (W(e.Center + Vec2.FromAngle(s + sweep) * e.R) - c).Angle;
                var mirrored = Mirrored(W);
                Add(e, mirrored ? new ArcEntity(layer, c, r, a1, a0) : new ArcEntity(layer, c, r, a0, a1));
                return;
            }
            // 楕円・楕円弧 (扁平率と傾き角) は折れ線で近似
            var (start, arc) = e.Full ? (0.0, GeoMath.TwoPi) : (e.Start, e.Arc);
            var n = Math.Max(8, (int)(Math.Abs(arc) / (Math.PI / 36)));
            var pts = new List<Vec2>();
            for (var i = 0; i <= n; i++)
            {
                var t = start + arc * i / n;
                var p = new Vec2(Math.Cos(t) * e.R, Math.Sin(t) * e.R * e.Flat).Rotate(e.Tilt) + e.Center;
                pts.Add(W(p));
            }
            if (e.Full) pts.RemoveAt(pts.Count - 1);
            Add(e, new PolylineEntity(layer, pts, e.Full));
        }

        private static bool Mirrored(Func<Vec2, Vec2> W)
        {
            var o = W(Vec2.Zero);
            return (W(new Vec2(1, 0)) - o).Cross(W(new Vec2(0, 1)) - o) < 0;
        }

        private void EmitText(Moji t, Layer layer, Func<Vec2, Vec2> W, double k, Func<Vec2, Vec2>? m)
        {
            if (t.Text.StartsWith("^@", StringComparison.Ordinal))
            {
                Skip("画像・特殊文字 (^@)");
                return;
            }
            if (string.IsNullOrWhiteSpace(t.Text) || t.SizeY <= 0) return;
            var p = W(t.Start);
            var end = W(t.End);
            var angle = m != null ? (end - p).Angle : t.AngleDeg * Math.PI / 180;
            var text = new TextEntity(layer, p, t.Text, t.SizeY * k, angle)
            {
                FontName = string.IsNullOrWhiteSpace(t.Font) || t.Font == TextEntity.DefaultFontName ? null : t.Font,
            };
            var width = (end - p).Length;
            if (width > 0) text.FitWidth = width; // Jw_cad の文字は固定ピッチ。終点までの幅に合わせる
            if ((t.Flag & 0x0020) != 0) Skip("縦書き文字 (横書きで表示)");
            Add(t, text);
        }

        private void EmitSolid(Solid s, Layer layer, Func<Vec2, Vec2> W, double k)
        {
            List<Vec2> loop;
            if (s.Style >= 101)
            {
                // 円のソリッド: m_start = 中心, m_end = (半径, 扁平率), m_DPoint2 = (傾き角, 開始角), m_DPoint3 = (円弧角, 種別)
                var (c, r, flat, tilt, start, arc) = (s.P1, s.P4.X, s.P4.Y, s.P2.X, s.P2.Y, s.P3.X);
                if (flat <= 0) flat = 1;
                var full = s.P3.Y == 100 || Math.Abs(arc) < 1e-12;
                if (full) (start, arc) = (0, GeoMath.TwoPi);
                var n = Math.Max(12, (int)(Math.Abs(arc) / (Math.PI / 36)));
                loop = [];
                if (!full && s.P3.Y == 0 && s.Style == 101) loop.Add(W(c)); // 扇形
                for (var i = 0; i <= n; i++)
                {
                    var t = start + arc * i / n;
                    loop.Add(W(new Vec2(Math.Cos(t) * r, Math.Sin(t) * r * flat).Rotate(tilt) + c));
                }
                if (s.Style is 105 or 106 or 111) Skip("円環・円周のソリッド (外形を塗りつぶし)");
            }
            else
            {
                loop = [W(s.P1), W(s.P2), W(s.P3), W(s.P4)];
            }
            var cleaned = new List<Vec2>();
            foreach (var p in loop)
                if (cleaned.Count == 0 || cleaned[^1].DistanceTo(p) > 1e-12) cleaned.Add(p);
            if (cleaned.Count >= 2 && cleaned[0].DistanceTo(cleaned[^1]) < 1e-12) cleaned.RemoveAt(cleaned.Count - 1);
            if (cleaned.Count < 3) return;
            var hatch = new HatchEntity(layer, [cleaned], [], solid: true);
            if (s.Rgb is uint rgb) hatch.Color = FromColorRef(rgb);
            _out.Add(Styled(s, hatch, colorFromPen: s.Rgb == null));
        }

        private void Add(Figure f, Entity e) => _out.Add(Styled(f, e, colorFromPen: true));

        private Entity Styled(Figure f, Entity e, bool colorFromPen)
        {
            if (colorFromPen) e.Color = ColorOf(f.Color);
            if (f is Sen or Enko) e.LineType = LineTypeOf(f.Style);
            // 1/100 mm 単位の線幅 (文字のこの欄は寸法値設定のフラグなので使わない)
            if (f is not Moji && f.Width > 0 && d.H.Version >= 351 && d.H.MaxDrawWid == -101) e.LineWeight = f.Width / 100.0;
            if ((f.Flag & 0x0020) != 0 && e is LineEntity) e.Tag ??= HatchTag;
            return e;
        }

        private RgbColor? ColorOf(ushort pen)
        {
            if (pen is >= 1 and <= 9) return PenColors[pen];
            if (pen is >= 100 and <= 356 && d.H.Version >= 420)
            {
                var c = FromColorRef(d.H.SxfScreenColors[pen - 100]);
                return IsDark(c) ? RgbColor.White : c;
            }
            return null;
        }
    }

    private static RgbColor FromColorRef(uint c) => new((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
    private static uint ToColorRef(RgbColor c) => c.R | (uint)c.G << 8 | (uint)c.B << 16;
    private static bool IsDark(RgbColor c) => c.R + c.G + c.B < 96;

    /// <summary>
    /// ファイルメモに入れる、用紙の中心の図面座標 (m)。JWW の座標は用紙の中心が原点なので、
    /// 測量座標の図面を保存して読み直しても元の位置に戻るように覚えておく (Jw_cad では用紙の上に表示される)。
    /// </summary>
    private const string OriginMemoPrefix = "CivilCad 原点 ";

    private static string OriginMemo(Vec2 origin) =>
        OriginMemoPrefix + origin.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + origin.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static Vec2 OriginFromMemo(string memo)
    {
        var m = OriginMemoRegex().Match(memo);
        if (!m.Success) return Vec2.Zero;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out var x) &&
               double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, inv, out var y) && double.IsFinite(x) && double.IsFinite(y)
            ? new Vec2(x, y)
            : Vec2.Zero;
    }

    [GeneratedRegex(@"CivilCad 原点 ([-+0-9.Ee]+),([-+0-9.Ee]+)")]
    private static partial Regex OriginMemoRegex();

    /// <summary>以前の版で JWW の実点から作った円の印。保存すると実点に戻す (今は点 (PointEntity) として読む)。</summary>
    public const string PointTag = "JWW:POINT";

    /// <summary>JWW でハッチ属性の付いていた線。</summary>
    public const string HatchTag = "JWW:HATCH";
}
