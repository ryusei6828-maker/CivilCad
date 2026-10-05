using System.IO;
using System.Globalization;
using System.Text;

namespace CivilCad.App.IO;

/// <summary>STEP (ISO 10303-21) のパラメータ。</summary>
internal abstract record StepValue
{
    public static readonly StepValue Null = new StepNull();
}

internal sealed record StepNull : StepValue;           // $ と *
internal sealed record StepNumber(double Value) : StepValue;
internal sealed record StepString(string Value) : StepValue;
internal sealed record StepEnum(string Value) : StepValue;     // .T. などの点を除いた名前
internal sealed record StepRef(int Id) : StepValue;
internal sealed record StepList(List<StepValue> Items) : StepValue;
internal sealed record StepTyped(string Name, StepValue Value) : StepValue; // POSITIVE_LENGTH_MEASURE(0.13) など

/// <summary>エンティティの 1 つのレコード (名前と引数)。</summary>
internal sealed record StepRecord(string Name, List<StepValue> Params)
{
    public StepValue this[int i] => i < Params.Count ? Params[i] : StepValue.Null;
}

/// <summary>インスタンス。単純なものはレコード 1 つ、複合 (#n=(A()B()…)) は複数。</summary>
internal sealed record StepInstance(int Id, List<StepRecord> Records)
{
    public StepRecord? Get(string name) => Records.FirstOrDefault(r => r.Name == name);
    public bool Has(string name) => Records.Any(r => r.Name == name);
    public string Name => Records.Count == 1 ? Records[0].Name : "";
}

/// <summary>STEP ファイルの DATA 部を読む。文字列の \X2\…\X0\ (UTF-16)・\X\hh・\S\ などは戻す。</summary>
internal sealed class StepFile
{
    public Dictionary<int, StepInstance> Instances { get; } = [];
    public List<int> Order { get; } = [];
    public string Header { get; private set; } = "";

    public StepInstance? this[int id] => Instances.GetValueOrDefault(id);
    public StepInstance? this[StepValue v] => v is StepRef r ? Instances.GetValueOrDefault(r.Id) : null;

    public static StepFile Parse(string text)
    {
        var f = new StepFile();
        var data = text.IndexOf("DATA;", StringComparison.OrdinalIgnoreCase);
        if (!text.TrimStart().StartsWith("ISO-10303-21", StringComparison.OrdinalIgnoreCase) || data < 0)
            throw new InvalidDataException("STEP (ISO 10303-21) のファイルではありません。");
        f.Header = text[..data];
        var p = new Parser(text, data + 5);
        while (true)
        {
            p.SkipSpace();
            if (p.AtEnd || p.Peek() != '#') break; // ENDSEC;
            var id = p.InstanceId();
            p.SkipSpace();
            p.Expect('=');
            p.SkipSpace();
            var records = new List<StepRecord>();
            if (p.Peek() == '(')
            {
                // 複合インスタンス
                p.Next();
                while (true)
                {
                    p.SkipSpace();
                    if (p.Peek() == ')') { p.Next(); break; }
                    records.Add(p.Record());
                }
            }
            else
            {
                records.Add(p.Record());
            }
            p.SkipSpace();
            p.Expect(';');
            f.Instances[id] = new StepInstance(id, records);
            f.Order.Add(id);
        }
        return f;
    }

    private sealed class Parser(string s, int i)
    {
        private int _i = i;

        public bool AtEnd => _i >= s.Length;
        public char Peek() => _i < s.Length ? s[_i] : '\0';
        public char Next() => _i < s.Length ? s[_i++] : throw Error("ファイルが途中で終わっています");

        public void SkipSpace()
        {
            while (_i < s.Length)
            {
                if (char.IsWhiteSpace(s[_i])) _i++;
                else if (s[_i] == '/' && _i + 1 < s.Length && s[_i + 1] == '*')
                {
                    var end = s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    _i = end < 0 ? s.Length : end + 2;
                }
                else break;
            }
        }

        public void Expect(char c)
        {
            if (Peek() != c) throw Error($"'{c}' がありません");
            _i++;
        }

        private InvalidDataException Error(string what) => new($"P21 の読み込みに失敗しました: {what} (位置 {_i})。");

        public int InstanceId()
        {
            Expect('#');
            var start = _i;
            while (_i < s.Length && char.IsAsciiDigit(s[_i])) _i++;
            return int.Parse(s.AsSpan(start, _i - start), CultureInfo.InvariantCulture);
        }

        private string Keyword()
        {
            var start = _i;
            while (_i < s.Length && (char.IsAsciiLetterOrDigit(s[_i]) || s[_i] is '_' or '!')) _i++;
            if (_i == start) throw Error("名前がありません");
            return s[start.._i].ToUpperInvariant();
        }

        public StepRecord Record()
        {
            var name = Keyword();
            SkipSpace();
            return new StepRecord(name, List());
        }

        private List<StepValue> List()
        {
            Expect('(');
            var items = new List<StepValue>();
            SkipSpace();
            if (Peek() == ')') { _i++; return items; }
            while (true)
            {
                SkipSpace();
                items.Add(Value());
                SkipSpace();
                var c = Next();
                if (c == ')') return items;
                if (c != ',') throw Error("',' がありません");
            }
        }

        private StepValue Value()
        {
            var c = Peek();
            switch (c)
            {
                case '$' or '*':
                    _i++;
                    return StepValue.Null;
                case '#':
                    return new StepRef(InstanceId());
                case '\'':
                    return new StepString(StringLiteral());
                case '.':
                {
                    _i++;
                    var start = _i;
                    while (_i < s.Length && s[_i] != '.') _i++;
                    var v = s[start.._i];
                    _i++;
                    return new StepEnum(v.ToUpperInvariant());
                }
                case '(':
                    return new StepList(List());
                case '"':
                {
                    // バイナリ (使わない)
                    var end = s.IndexOf('"', _i + 1);
                    _i = end < 0 ? s.Length : end + 1;
                    return StepValue.Null;
                }
            }
            if (char.IsAsciiLetter(c))
            {
                var name = Keyword();
                SkipSpace();
                var inner = List();
                return new StepTyped(name, inner.Count == 1 ? inner[0] : new StepList(inner));
            }
            var st = _i;
            while (_i < s.Length && (char.IsAsciiDigit(s[_i]) || s[_i] is '+' or '-' or '.' or 'E' or 'e')) _i++;
            if (_i == st) throw Error($"値を読めません ('{c}')");
            return new StepNumber(double.Parse(s.AsSpan(st, _i - st), NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        /// <summary>'…' ('' は ')。\X2\hhhh…\X0\ は UTF-16、\X\hh は ISO 8859-1、\S\c は c+128、\\ は \。</summary>
        private string StringLiteral()
        {
            Expect('\'');
            var sb = new StringBuilder();
            while (true)
            {
                var ch = Next();
                if (ch == '\'')
                {
                    if (Peek() == '\'') { _i++; sb.Append('\''); continue; }
                    return sb.ToString();
                }
                if (ch != '\\')
                {
                    sb.Append(ch);
                    continue;
                }
                if (Rest("X2\\"))
                {
                    _i += 3;
                    while (_i + 4 <= s.Length && !Rest("\\X0\\"))
                    {
                        sb.Append((char)int.Parse(s.AsSpan(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _i += 4;
                    }
                    if (Rest("\\X0\\")) _i += 4;
                }
                else if (Rest("X4\\"))
                {
                    _i += 3;
                    while (_i + 8 <= s.Length && !Rest("\\X0\\"))
                    {
                        sb.Append(char.ConvertFromUtf32(int.Parse(s.AsSpan(_i, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                        _i += 8;
                    }
                    if (Rest("\\X0\\")) _i += 4;
                }
                else if (Rest("X\\") && _i + 4 <= s.Length)
                {
                    sb.Append((char)int.Parse(s.AsSpan(_i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    _i += 4;
                }
                else if (Rest("S\\") && _i + 3 <= s.Length)
                {
                    sb.Append((char)(s[_i + 2] + 128));
                    _i += 3;
                }
                else if (Rest("\\"))
                {
                    sb.Append('\\');
                    _i++;
                }
                else if (_i + 2 <= s.Length && s[_i] == 'P' && s[_i + 1] != '\\' && _i + 2 < s.Length && s[_i + 2] == '\\')
                {
                    _i += 3; // \PA\ などのコードページ指定は無視
                }
                else
                {
                    sb.Append('\\');
                }
            }
        }

        private bool Rest(string t) => string.CompareOrdinal(s, _i, t, 0, t.Length) == 0;
    }
}

/// <summary>STEP の書き手 (#10, #20… の番号で 1 行 1 インスタンス)。</summary>
internal sealed class StepWriter
{
    private readonly StringBuilder _sb = new();
    private int _n;

    public string E(string text)
    {
        _n += 10;
        _sb.Append('#').Append(_n).Append('=').Append(text).Append(";\n");
        return "#" + _n;
    }

    public string Complex(params string[] records)
    {
        _n += 10;
        _sb.Append('#').Append(_n).Append("=(\n").Append(string.Join("\n", records)).Append("\n);\n");
        return "#" + _n;
    }

    public string Body => _sb.ToString();

    public static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
    public static string A(double v) => v.ToString("F14", CultureInfo.InvariantCulture);

    /// <summary>文字列: ASCII 以外は \X2\ 表記、' は ''。</summary>
    public static string S(string s)
    {
        var sb = new StringBuilder("'");
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] < 0x7F && s[i] >= 0x20)
            {
                sb.Append(s[i] == '\'' ? "''" : s[i] == '\\' ? "\\\\" : s[i].ToString());
                i++;
                continue;
            }
            sb.Append("\\X2\\");
            while (i < s.Length && (s[i] >= 0x7F || s[i] < 0x20)) sb.Append(((int)s[i++]).ToString("X4"));
            sb.Append("\\X0\\");
        }
        return sb.Append('\'').ToString();
    }
}
