using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

public static partial class JwwIO
{
    /// <summary>
    /// JWW (データ版 700 = Jw_cad 7 以降) で保存する。図面全体を 1 つの縮尺 (図面の縮尺) で用紙に置き、
    /// 用紙は SFC と同じ選び方 (図枠に合う A 判、無ければ図形が収まる A 判) で、その中心を JWW の原点にする。
    /// レイヤは「g-l 名前」(16 進) の名前ならそのグループ・レイヤに、それ以外は空いているところに順に割り当てる。
    /// ポリラインは線分に、ハッチングの模様は線 (ハッチ属性) に、塗りつぶしは三角形のソリッドにする。
    /// </summary>
    public static void Save(CadDocument doc, string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllBytes(path, new Exporter(doc).Build());
    }

    /// <summary>「g-l 名前」のレイヤ名 (読み込んだ JWW のレイヤ)。</summary>
    [GeneratedRegex(@"^([0-9A-Fa-f])-([0-9A-Fa-f])(?: (.*))?$")]
    private static partial Regex LayerNameRegex();

    private sealed class ArchiveWriter
    {
        private readonly MemoryStream _s = new();
        private readonly BinaryWriter _w;
        private readonly Dictionary<string, int> _classes = [];
        private int _next = 1;
        private static readonly Encoding Sjis = Encoding.GetEncoding(932, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);

        public ArchiveWriter() => _w = new BinaryWriter(_s);

        public void Raw(byte[] b) => _w.Write(b);
        public void U8(int v) => _w.Write((byte)v);
        public void U16(int v) => _w.Write((ushort)v);
        public void U32(long v) => _w.Write(unchecked((uint)v));
        public void F64(double v) => _w.Write(v);
        public void P(Vec2 p) { F64(p.X); F64(p.Y); }

        public void Str(string s)
        {
            var raw = Sjis.GetBytes(s);
            if (raw.Length < 0xFF) U8(raw.Length);
            else if (raw.Length < 0xFFFE) { U8(0xFF); U16(raw.Length); }
            else { U8(0xFF); U16(0xFFFF); U32(raw.Length); }
            _w.Write(raw);
        }

        public void Count(int n)
        {
            if (n < 0xFFFF) U16(n);
            else { U16(0xFFFF); U32(n); }
        }

        /// <summary>オブジェクトのタグ (初出のクラスは 0xFFFF・スキーマ・名前、以後は 0x8000 | 番号)。</summary>
        public void Tag(string cls)
        {
            if (_classes.TryGetValue(cls, out var index))
            {
                if (index < 0x7FFF) U16(0x8000 | index);
                else { U16(0x7FFF); U32(0x80000000u | (uint)index); }
            }
            else
            {
                U16(0xFFFF);
                U16(SaveVersion); // スキーマ (Jw_cad はデータの版と同じ値)
                U16(cls.Length);
                _w.Write(Encoding.ASCII.GetBytes(cls));
                _classes[cls] = _next++;
            }
            _next++; // オブジェクトも番号を 1 つ使う
        }

        public byte[] ToArray()
        {
            _w.Flush();
            return _s.ToArray();
        }
    }

    private sealed class Exporter(CadDocument doc)
    {
        private readonly ArchiveWriter _a = new();
        private readonly Dictionary<Layer, (int G, int L)> _slots = [];
        private readonly string[,] _layerNames = new string[16, 16];
        private readonly bool[,] _layerHidden = new bool[16, 16];
        private readonly List<RgbColor> _sxfColors = []; // 線色 100 番以降 (SXF 対応拡張線色) に入れる色
        private readonly List<Action> _figures = [];
        private double _k = 1;        // 図寸 1mm = _k [m]
        private Vec2 _center;         // 用紙の中心 (図面座標 m)

        public byte[] Build()
        {
            var (paper, pw, ph, origin) = SfcWriter.Layout(doc);
            _k = doc.ScaleDenominator / 1000;
            _center = origin + new Vec2(pw, ph) / 2 * _k;
            AssignLayers();
            // プリセット (記号) は線・塗り・円・文字に展開する。画像は書かない
            foreach (var e in doc.Entities.Where(e => e is not ImageEntity).SelectMany(e => e is SymbolEntity s ? s.Explode() : [e])) AddEntity(e);

            _a.Raw(Encoding.ASCII.GetBytes("JwwData."));
            WriteHeader(PaperCode(paper, pw, ph));
            _a.Count(_figures.Count);
            foreach (var f in _figures) f();
            _a.Count(0); // ブロック定義
            _a.U32(0);   // 画像 (Ver.7.00 以降)
            return _a.ToArray();
        }

        private static int PaperCode(Drawing.PaperSize? paper, double w, double h) => paper?.Name switch
        {
            "A0" => 0, "A1" => 1, "A2" => 2, "A3" => 3, "A4" => 4,
            _ => w <= 1682 && h <= 1189 ? 8 : w <= 2378 && h <= 1682 ? 9 : w <= 3364 && h <= 2378 ? 10 : w <= 4756 && h <= 3364 ? 11
                : w <= 10000 ? 12 : w <= 50000 ? 13 : 14,
        };

        private void AssignLayers()
        {
            var used = new bool[16, 16];
            var rest = new List<Layer>();
            foreach (var l in doc.Layers)
            {
                if (l.Name == "0" && !used[0, 0])
                {
                    // 既定のレイヤ「0」は 0-0 (名前なし)
                    used[0, 0] = true;
                    _slots[l] = (0, 0);
                    _layerNames[0, 0] = "";
                    _layerHidden[0, 0] = !l.IsVisible;
                    continue;
                }
                var m = LayerNameRegex().Match(l.Name);
                if (m.Success)
                {
                    int g = Convert.ToInt32(m.Groups[1].Value, 16), n = Convert.ToInt32(m.Groups[2].Value, 16);
                    if (!used[g, n])
                    {
                        used[g, n] = true;
                        _slots[l] = (g, n);
                        _layerNames[g, n] = m.Groups[3].Success ? m.Groups[3].Value : "";
                        _layerHidden[g, n] = !l.IsVisible;
                        continue;
                    }
                }
                rest.Add(l);
            }
            var slot = 0;
            foreach (var l in rest)
            {
                while (slot < 256 && used[slot / 16, slot % 16]) slot++;
                var (g, n) = slot < 256 ? (slot / 16, slot % 16) : (15, 15); // 256 を超えたら最後のレイヤにまとめる
                if (slot < 256)
                {
                    used[g, n] = true;
                    _layerNames[g, n] = l.Name;
                    _layerHidden[g, n] = !l.IsVisible;
                }
                _slots[l] = (g, n);
            }
        }

        private Vec2 Paper(Vec2 world) => (world - _center) / _k;

        private int PenColor(Entity e)
        {
            var c = e.EffectiveColor;
            if (c == RgbColor.White) return 2;
            var i = Array.IndexOf(PenColors, c);
            if (i is >= 1 and <= 9) return i;
            var u = _sxfColors.IndexOf(c);
            if (u < 0 && _sxfColors.Count < 257)
            {
                _sxfColors.Add(c);
                u = _sxfColors.Count - 1;
            }
            return u >= 0 ? 100 + u : 2;
        }

        private void Base(Entity e, int style = 1, int? color = null, int flag = 0, bool text = false)
        {
            var (g, l) = _slots.TryGetValue(e.Layer, out var s) ? s : (0, 0);
            _a.U32(0);
            _a.U8(style);
            _a.U16(color ?? PenColor(e));
            // 線幅 (1/100 mm 単位の設定で保存する)。文字ではこの欄は寸法値設定のフラグなので 0
            _a.U16(!text && e.LineWeight is double w ? (int)Math.Round(w * 100) : 0);
            _a.U16(l);
            _a.U16(g);
            _a.U16(flag);
        }

        private void Sen(Entity e, Vec2 a, Vec2 b, int flag = 0) => _figures.Add(() =>
        {
            _a.Tag("CDataSen");
            Base(e, flag: flag);
            _a.P(Paper(a));
            _a.P(Paper(b));
        });

        private void AddEntity(Entity e)
        {
            PenColor(e); // 拡張線色の表はヘッダーに書くので、先に登録しておく
            switch (e)
            {
                case LineEntity l:
                    Sen(e, l.Start, l.End);
                    break;
                case PolylineEntity pl:
                    foreach (var (a, b) in pl.Segments)
                        if (a.DistanceTo(b) > 0) Sen(e, a, b);
                    break;
                case CircleEntity c when c.Tag == PointTag:
                    _figures.Add(() =>
                    {
                        _a.Tag("CDataTen");
                        Base(e);
                        _a.P(Paper(c.Center));
                        _a.U32(0);
                    });
                    break;
                case CircleEntity c:
                    Enko(e, c.Center, c.Radius, 0, GeoMath.TwoPi, full: true);
                    break;
                case ArcEntity a:
                    Enko(e, a.Center, a.Radius, GeoMath.NormalizeAngle(a.StartAngle), a.Sweep, full: false);
                    break;
                case TextEntity t when !string.IsNullOrEmpty(t.Text):
                    AddText(t);
                    break;
                case HatchEntity h:
                    AddHatch(h);
                    break;
            }
        }

        private void Enko(Entity e, Vec2 center, double r, double start, double arc, bool full) => _figures.Add(() =>
        {
            _a.Tag("CDataEnko");
            Base(e);
            _a.P(Paper(center));
            _a.F64(r / _k);
            _a.F64(start);
            _a.F64(arc);
            _a.F64(0); // 傾き角
            _a.F64(1); // 扁平率
            _a.U32(full ? 1 : 0);
        });

        private void AddText(TextEntity t)
        {
            // Jw_cad の文字は始点 (左下) と終点で置く。全角 1 文字の幅 = 文字幅 (sizeX)
            var width = t.ApproxWidth;
            var start = t.BaselineStart(width);
            var end = start + Vec2.FromAngle(t.Rotation) * width;
            var units = t.Text.Sum(c => c < 0x80 || c is >= '｡' and <= 'ﾟ' ? 0.5 : 1.0);
            var sizeY = t.Height / _k;
            var sizeX = units > 0 ? width / _k / units : sizeY;
            _figures.Add(() =>
            {
                _a.Tag("CDataMoji");
                Base(t, style: 0, text: true);
                _a.P(Paper(start));
                _a.P(Paper(end));
                _a.U32(1);       // 文字種 (任意サイズは 1 と同じ扱い)
                _a.F64(sizeX);
                _a.F64(sizeY);
                _a.F64(0);       // 文字間隔
                _a.F64(GeoMath.NormalizeAngle(t.Rotation) * 180 / Math.PI);
                _a.Str("ＭＳ ゴシック");
                _a.Str(t.Text);
            });
        }

        private void AddHatch(HatchEntity h)
        {
            if (!h.Solid)
            {
                // 模様は線 (ハッチ属性)、境界は描かない (CivilCad と同じ見え方)
                foreach (var (a, b) in h.Lines) Sen(h, a, b, flag: 0x0020);
                return;
            }
            if (h.Loops.Count != 1)
            {
                // 中抜きのある塗りつぶしは JWW のソリッドで表せないので、境界の線にする
                foreach (var (a, b) in h.BoundarySegments) Sen(h, a, b);
                return;
            }
            var color = h.EffectiveColor;
            foreach (var (p, q, r) in Triangulate(h.Loops[0]))
                _figures.Add(() =>
                {
                    _a.Tag("CDataSolid");
                    Base(h, color: 10); // 任意色
                    _a.P(Paper(p));
                    _a.P(Paper(r)); // 第 4 点 (三角形なので第 3 点と同じ)
                    _a.P(Paper(q));
                    _a.P(Paper(r));
                    _a.U32(ToColorRef(color == RgbColor.White ? new RgbColor(0, 0, 0) : color));
                });
        }

        // ---- ヘッダー (ReadHeader と同じ順) ----

        private void WriteHeader(int paperCode)
        {
            var a = _a;
            a.U32(SaveVersion);
            a.Str(OriginMemo(_center)); // ファイルメモ (用紙の中心の図面座標)
            a.U32(paperCode);
            a.U32(0); // 書込レイヤグループ
            for (var g = 0; g < 16; g++)
            {
                a.U32(g == 0 ? 3 : 2);    // 状態 (3: 書込、2: 編集可能)
                a.U32(0);                 // 書込レイヤ
                a.F64(doc.ScaleDenominator);
                a.U32(0);
                for (var l = 0; l < 16; l++)
                {
                    a.U32(_layerHidden[g, l] ? 0 : (g, l) == (0, 0) ? 3 : 2);
                    a.U32(0);
                }
            }
            for (var i = 0; i < 14 + 5 + 1; i++) a.U32(0);
            a.U32(unchecked((uint)-101)); // 線幅を 1/100 mm 単位とする
            a.F64(0); a.F64(0); a.F64(1); // プリンタ出力範囲の原点・倍率
            a.U32(0); a.U32(0);
            a.F64(0); a.F64(0); a.F64(0); a.F64(0); a.F64(0);
            for (var g = 0; g < 16; g++)
                for (var l = 0; l < 16; l++)
                    a.Str(_layerNames[g, l] ?? "");
            for (var g = 0; g < 16; g++) a.Str("");
            a.F64(0); a.F64(35); a.U32(0); a.F64(0); // 日影 (緯度 35°)
            a.F64(0); a.F64(50);                     // 天空図
            a.U32(0);
            a.F64(1); a.F64(0); a.F64(0);            // 画面倍率・原点
            a.F64(1); a.F64(0); a.F64(0);            // 範囲記憶
            for (var n = 0; n < 8; n++) { a.F64(1); a.F64(0); a.F64(0); a.U32(0); } // マークジャンプ
            a.F64(0); a.F64(0); a.F64(0); a.U32(0); a.F64(0); a.F64(0); a.F64(0); a.U32(0);
            for (var n = 0; n < 10; n++) a.F64(1);   // 複線間隔
            a.F64(0);
            for (var n = 0; n < 10; n++) { a.U32(ScreenColor(n)); a.U32(1); }
            int[] printWidths = [0, 13, 18, 25, 35, 50, 70, 18, 25, 13];
            for (var n = 0; n < 10; n++) { a.U32(n == 0 ? 0xFFFFFF : 0); a.U32(printWidths[n]); a.F64(0.4); }
            (uint Mask, int Unit)[] types = [(0x99999999, 4), (0xC3C3C3C3, 8), (0xE7E7E7E7, 8), (0xF99FF99F, 16), (0xFFF8FFF8, 32), (0xF3F3F3F3, 8), (0xFF9FF9FF, 32), (0x22222222, 4)];
            foreach (var (mask, unit) in types) { a.U32(mask); a.U32(unit); a.U32(1); a.U32(10); } // 線種 2〜9
            for (var n = 0; n < 5; n++) { a.U32(0xFFFFFFFF); a.U32(10); a.U32(1); a.U32(10); a.U32(10); } // ランダム線
            for (var n = 0; n < 4; n++) { a.U32(types[n + 4].Mask); a.U32(types[n + 4].Unit); a.U32(2); a.U32(20); } // 倍長線種
            a.U32(0); a.U32(1);
            for (var i = 0; i < 9; i++) a.U32(0);
            a.U32(0); a.U32(0); a.U32(0); a.U32(0); a.U32(0); // 作図時間・2.5D
            for (var i = 0; i < 5; i++) a.F64(0);
            for (var i = 0; i < 4; i++) a.F64(0);    // 長さ・矩形・半径の最終値
            a.U32(0); a.U32(0);                      // ソリッドの任意色
            for (var n = 0; n <= 256; n++)           // SXF 対応拡張線色 (画面)
            {
                a.U32(n < _sxfColors.Count ? ToColorRef(_sxfColors[n]) : 0);
                a.U32(1);
            }
            for (var n = 0; n <= 256; n++)           // SXF 対応拡張線色 (プリンタ)
            {
                a.Str(n < _sxfColors.Count ? $"CivilCad{n + 1}" : "");
                a.U32(n < _sxfColors.Count ? ToColorRef(_sxfColors[n]) : 0);
                a.U32(25);
                a.F64(0.4);
            }
            for (var n = 0; n <= 32; n++) { a.U32(0xFFFFFFFF); a.U32(16); a.U32(1); a.U32(10); } // SXF 対応拡張線種
            for (var n = 0; n <= 32; n++)
            {
                a.Str("");
                a.U32(0);
                for (var j = 0; j < 10; j++) a.F64(0);
            }
            double[] mojiSizes = [2, 2.5, 3, 4, 5, 6, 7, 8, 10, 1];
            foreach (var s in mojiSizes) { a.F64(s); a.F64(s); a.F64(0); a.U32(2); } // 文字種 1〜10
            a.F64(3); a.F64(3); a.F64(0); a.U32(2); a.U32(1);
            a.F64(0); a.F64(0);
            a.U32(0);
            for (var i = 0; i < 6; i++) a.F64(0);
        }

        /// <summary>画面表示色 (白い背景の Jw_cad 向けの既定値)。</summary>
        private static uint ScreenColor(int n) => n switch
        {
            0 => 0xFFFFFF,
            1 => ToColorRef(new RgbColor(0, 160, 200)),
            2 => 0,
            3 => ToColorRef(new RgbColor(0, 150, 0)),
            4 => ToColorRef(new RgbColor(200, 150, 0)),
            5 => ToColorRef(new RgbColor(200, 0, 200)),
            6 => ToColorRef(new RgbColor(0, 0, 255)),
            7 => ToColorRef(new RgbColor(0, 128, 128)),
            8 => ToColorRef(new RgbColor(230, 0, 0)),
            _ => ToColorRef(new RgbColor(160, 160, 160)),
        };
    }

    /// <summary>単純な多角形を三角形に分ける (耳切り法)。</summary>
    public static List<(Vec2, Vec2, Vec2)> Triangulate(IReadOnlyList<Vec2> polygon)
    {
        var pts = polygon.ToList();
        if (pts.Count >= 2 && pts[0].DistanceTo(pts[^1]) < 1e-12) pts.RemoveAt(pts.Count - 1);
        var result = new List<(Vec2, Vec2, Vec2)>();
        if (pts.Count < 3) return result;
        if (HatchEntity.SignedArea(pts) < 0) pts.Reverse(); // 反時計回りにそろえる
        var guard = pts.Count * pts.Count;
        while (pts.Count > 3 && guard-- > 0)
        {
            var cut = false;
            for (var i = 0; i < pts.Count; i++)
            {
                var (a, b, c) = (pts[(i + pts.Count - 1) % pts.Count], pts[i], pts[(i + 1) % pts.Count]);
                if ((b - a).Cross(c - b) <= 1e-15) continue; // 凹んだ角
                var inside = false;
                for (var j = 0; j < pts.Count && !inside; j++)
                {
                    var p = pts[j];
                    if (p == a || p == b || p == c) continue;
                    inside = (b - a).Cross(p - a) >= 0 && (c - b).Cross(p - b) >= 0 && (a - c).Cross(p - c) >= 0;
                }
                if (inside) continue;
                result.Add((a, b, c));
                pts.RemoveAt(i);
                cut = true;
                break;
            }
            if (!cut) break; // 自己交差など: 残りは扇形で埋める
        }
        for (var i = 1; i + 1 < pts.Count; i++) result.Add((pts[0], pts[i], pts[i + 1]));
        return result;
    }
}
