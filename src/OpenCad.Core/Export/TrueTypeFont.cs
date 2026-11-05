using System.Text;

namespace OpenCad.Core.Export;

/// <summary>
/// Đọc tối thiểu một file TrueType để phục vụ nhúng vào PDF:
/// bảng cmap (ký tự → glyph id), hmtx (độ rộng), head/hhea (metric).
/// Đủ để dựng CIDFontType2 với Encoding Identity-H, nhờ đó giữ được dấu tiếng Việt.
/// </summary>
public sealed class TrueTypeFont
{
    public byte[] Data { get; }
    public string PostScriptName { get; private set; } = "OpenCadFont";

    public ushort UnitsPerEm { get; private set; } = 1000;
    public short XMin { get; private set; }
    public short YMin { get; private set; }
    public short XMax { get; private set; } = 1000;
    public short YMax { get; private set; } = 1000;
    public short Ascender { get; private set; } = 800;
    public short Descender { get; private set; } = -200;
    public short CapHeight { get; private set; } = 700;

    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<char, ushort> _glyphCache = new();

    private int _cmapSubtable = -1;
    private int _segCount;
    private int _endCodes;
    private int _startCodes;
    private int _idDelta;
    private int _idRangeOffset;
    private int _numberOfHMetrics;
    private int _hmtxOffset;

    private TrueTypeFont(byte[] data) => Data = data;

    public static TrueTypeFont? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var font = new TrueTypeFont(File.ReadAllBytes(path));
            if (!font.Parse()) return null;

            return font.HasCmap ? font : null;
        }
        catch
        {
            return null;
        }
    }

    public bool HasCmap => _cmapSubtable > 0;
    public bool HasGlyphs => _hmtxOffset > 0;

    // -------------------------------------------------------------- PARSING

    private bool Parse()
    {
        if (Data.Length < 12) return false;

        var version = U32(0);
        if (version != 0x00010000 && version != 0x74727565) return false;   // 'true'

        var numTables = U16(4);
        if (numTables == 0 || 12 + numTables * 16 > Data.Length) return false;

        for (var i = 0; i < numTables; i++)
        {
            var record = 12 + i * 16;
            var tag = Encoding.ASCII.GetString(Data, record, 4);
            var offset = (int)U32(record + 8);
            var length = (int)U32(record + 12);

            if (offset > 0 && offset < Data.Length)
                _tables[tag] = (offset, Math.Min(length, Data.Length - offset));
        }

        ParseHead();
        ParseHhea();
        ParseCmap();
        ParseName();

        return _tables.Count > 0;
    }

    private void ParseHead()
    {
        if (!_tables.TryGetValue("head", out var head) || head.Length < 54) return;

        UnitsPerEm = U16(head.Offset + 18);
        if (UnitsPerEm == 0) UnitsPerEm = 1000;

        XMin = S16(head.Offset + 36);
        YMin = S16(head.Offset + 38);
        XMax = S16(head.Offset + 40);
        YMax = S16(head.Offset + 42);
    }

    private void ParseHhea()
    {
        if (!_tables.TryGetValue("hhea", out var hhea) || hhea.Length < 36) return;

        Ascender = S16(hhea.Offset + 4);
        Descender = S16(hhea.Offset + 6);
        _numberOfHMetrics = U16(hhea.Offset + 34);

        if (_tables.TryGetValue("hmtx", out var hmtx))
            _hmtxOffset = hmtx.Offset;
    }

    private void ParseCmap()
    {
        if (!_tables.TryGetValue("cmap", out var cmap)) return;

        var count = U16(cmap.Offset + 2);
        var best = -1;
        var bestScore = -1;

        for (var i = 0; i < count; i++)
        {
            var record = cmap.Offset + 4 + i * 12;
            if (record + 12 > Data.Length) break;

            var platform = U16(record);
            var encoding = U16(record + 2);
            var subOffset = cmap.Offset + (int)U32(record + 4);

            if (subOffset + 4 > Data.Length) continue;
            if (U16(subOffset) != 4) continue;      // chỉ hỗ trợ format 4 (BMP)

            // (3,1) là Unicode BMP — ưu tiên nhất
            var score = (platform, encoding) switch
            {
                (3, 1) => 100,
                (0, 3) => 80,
                (0, 4) => 70,
                (3, 0) => 60,
                _ => platform == 3 ? 50 : 10,
            };

            if (score > bestScore)
            {
                bestScore = score;
                best = subOffset;
            }
        }

        if (best < 0) return;

        var segCountX2 = U16(best + 6);
        if (segCountX2 == 0) return;

        _cmapSubtable = best;
        _segCount = segCountX2 / 2;
        _endCodes = best + 14;
        _startCodes = _endCodes + segCountX2 + 2;
        _idDelta = _startCodes + segCountX2;
        _idRangeOffset = _idDelta + segCountX2;
    }

    private void ParseName()
    {
        if (!_tables.TryGetValue("name", out var name)) return;

        var count = U16(name.Offset + 2);
        var stringOffset = name.Offset + U16(name.Offset + 4);

        for (var i = 0; i < count; i++)
        {
            var record = name.Offset + 6 + i * 12;
            if (record + 12 > Data.Length) break;

            var platform = U16(record);
            var nameId = U16(record + 6);
            if (nameId != 6) continue;              // PostScript name

            var length = U16(record + 8);
            var offset = stringOffset + U16(record + 10);
            if (offset + length > Data.Length) continue;

            var raw = Encoding.ASCII.GetString(Data, offset, length);

            // Bản ghi platform 3 lưu UTF-16BE; lấy các byte chẵn
            if (platform == 3 && length > 1)
            {
                var sb = new StringBuilder(length / 2);
                for (var k = 0; k + 1 < length; k += 2) sb.Append((char)Data[offset + k]);
                raw = sb.ToString();
            }

            raw = new string(raw.Where(c => c > 32 && c < 127).ToArray());
            if (raw.Length > 0) PostScriptName = raw;
            return;
        }
    }

    // --------------------------------------------------------------- LOOKUP

    public ushort GlyphId(char c)
    {
        if (_glyphCache.TryGetValue(c, out var cached)) return cached;

        var gid = LookupGlyph(c);
        _glyphCache[c] = gid;
        return gid;
    }

    private ushort LookupGlyph(char c)
    {
        if (_cmapSubtable < 0 || c > 0xFFFF) return 0;

        for (var i = 0; i < _segCount; i++)
        {
            var end = U16(_endCodes + i * 2);
            if (end < c) continue;

            var start = U16(_startCodes + i * 2);
            if (start > c) return 0;

            var delta = S16(_idDelta + i * 2);
            var rangeOffset = U16(_idRangeOffset + i * 2);

            if (rangeOffset == 0)
                return (ushort)((c + delta) & 0xFFFF);

            var index = _idRangeOffset + i * 2 + rangeOffset + 2 * (c - start);
            if (index + 2 > Data.Length) return 0;

            var gid = U16(index);
            return gid == 0 ? (ushort)0 : (ushort)((gid + delta) & 0xFFFF);
        }

        return 0;
    }

    /// <summary>Độ rộng glyph theo đơn vị 1/1000 em (đơn vị PDF dùng).</summary>
    public ushort Width(ushort glyphId)
    {
        if (_hmtxOffset <= 0 || _numberOfHMetrics <= 0) return 500;

        var index = Math.Min(glyphId, _numberOfHMetrics - 1);
        var offset = _hmtxOffset + index * 4;
        if (offset + 2 > Data.Length) return 500;

        var advance = U16(offset);
        return (ushort)Math.Round(advance * 1000.0 / UnitsPerEm);
    }

    // ------------------------------------------------------------- READERS

    private ushort U16(int offset) => (ushort)((Data[offset] << 8) | Data[offset + 1]);
    private short S16(int offset) => (short)U16(offset);
    private uint U32(int offset) => ((uint)Data[offset] << 24) | ((uint)Data[offset + 1] << 16)
                                  | ((uint)Data[offset + 2] << 8) | Data[offset + 3];
}
