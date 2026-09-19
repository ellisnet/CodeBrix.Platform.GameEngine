using System;
using System.Text;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Rendering.Text;

/// <summary>
/// Reads a font's family name straight from its OpenType <c>name</c> table, so the same font file yields
/// the same family name on every platform.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SKTypeface.FamilyName"/> is NOT platform-neutral: it is whatever the platform's native font
/// back end reports. FreeType (Linux) reports the family name the file declares, while DirectWrite
/// (Windows) re-derives a weight/width/slope family and moves style words out of the name - a font whose
/// file declares "Kenney Future Narrow" is reported as "Kenney Future" there.
/// </para>
/// <para>
/// The rule applied here is fixed: the typographic family name (name ID 16) when the font declares one,
/// otherwise the legacy family name (name ID 1). Among the records for a name ID, a Windows-platform US
/// English record wins, then a Unicode-platform record, then a Windows-platform record in another language,
/// then a Macintosh Roman record.
/// </para>
/// </remarks>
internal static class FontFamilyNameReader
{
    /// <summary>The <c>name</c> table tag.</summary>
    private const uint NameTableTag = ('n' << 24) | ('a' << 16) | ('m' << 8) | 'e';

    private const ushort TypographicFamilyNameId = 16;
    private const ushort FamilyNameId = 1;

    private const ushort UnicodePlatform = 0;
    private const ushort MacintoshPlatform = 1;
    private const ushort WindowsPlatform = 3;

    private const ushort MacintoshRomanEncoding = 0;
    private const ushort MacintoshEnglishLanguage = 0;
    private const ushort WindowsUsEnglishLanguage = 0x0409;

    private const int HeaderSize = 6;
    private const int RecordSize = 12;

    /// <summary>
    /// The characters for Macintosh Roman bytes 0x80 to 0xFF; bytes below 0x80 are ASCII.
    /// </summary>
    internal const string MacRomanHighHalf =
        "ÄÅÇÉÑÖÜáàâäãåçéè" +
        "êëíìîïñóòôöõúùûü" +
        "†°¢£§•¶ß®©™´¨≠ÆØ" +
        "∞±≤≥¥µ∂∑∏π∫ªºΩæø" +
        "¿¡¬√ƒ≈∆«»… ÀÃÕŒœ" +
        "–—“”‘’÷◊ÿŸ⁄€‹›ﬁﬂ" +
        "‡·‚„‰ÂÊÁËÈÍÎÏÌÓÔ" +
        "ÒÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ";

    /// <summary>
    /// Gets the platform-neutral family name of a typeface.
    /// </summary>
    /// <param name="typeface">The typeface to name.</param>
    /// <returns>
    /// The family name from the font's <c>name</c> table, or <see cref="SKTypeface.FamilyName"/> when the
    /// font has no <c>name</c> table holding a readable family name.
    /// </returns>
    public static string GetFamilyName(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        if (typeface.TryGetTableData(NameTableTag, out byte[] nameTable))
        {
            string? familyName = ReadFamilyName(nameTable);
            if (familyName is not null)
                return familyName;
        }

        return typeface.FamilyName;
    }

    /// <summary>
    /// Reads the family name from the contents of an OpenType <c>name</c> table.
    /// </summary>
    /// <param name="nameTable">The complete <c>name</c> table.</param>
    /// <returns>The family name, or <see langword="null"/> when the table holds no readable family name.</returns>
    internal static string? ReadFamilyName(ReadOnlySpan<byte> nameTable)
    {
        return ReadName(nameTable, TypographicFamilyNameId)
            ?? ReadName(nameTable, FamilyNameId);
    }

    private static string? ReadName(ReadOnlySpan<byte> table, ushort nameId)
    {
        if (table.Length < HeaderSize)
            return null;

        int count = ReadUInt16(table, 2);
        int storageOffset = ReadUInt16(table, 4);

        string? best = null;
        int bestRank = int.MaxValue;

        for (int index = 0; index < count; index++)
        {
            int record = HeaderSize + (index * RecordSize);
            if (record + RecordSize > table.Length)
                break;

            if (ReadUInt16(table, record + 6) != nameId)
                continue;

            ushort platformId = ReadUInt16(table, record);
            ushort encodingId = ReadUInt16(table, record + 2);
            ushort languageId = ReadUInt16(table, record + 4);

            int rank = Rank(platformId, encodingId, languageId);
            if (rank >= bestRank)
                continue;

            int length = ReadUInt16(table, record + 8);
            int start = storageOffset + ReadUInt16(table, record + 10);
            if (start + length > table.Length)
                continue;

            string? name = Decode(table.Slice(start, length), platformId);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            best = name;
            bestRank = rank;
        }

        return best;
    }

    /// <summary>
    /// Ranks a name record by how authoritative its encoding and language are; lower is better, and
    /// <see cref="int.MaxValue"/> marks a record that is never read.
    /// </summary>
    private static int Rank(ushort platformId, ushort encodingId, ushort languageId)
    {
        //Windows-platform names are UTF-16 only for the Symbol, Unicode BMP and Unicode full encodings;
        //  the legacy East Asian encodings are never read
        bool windowsUnicode = platformId == WindowsPlatform && encodingId is 0 or 1 or 10;

        return platformId switch
        {
            WindowsPlatform when windowsUnicode && languageId == WindowsUsEnglishLanguage => 0,
            UnicodePlatform => 1,
            WindowsPlatform when windowsUnicode => 2,
            MacintoshPlatform when encodingId == MacintoshRomanEncoding
                && languageId == MacintoshEnglishLanguage => 3,
            MacintoshPlatform when encodingId == MacintoshRomanEncoding => 4,
            _ => int.MaxValue
        };
    }

    private static string? Decode(ReadOnlySpan<byte> bytes, ushort platformId)
    {
        string text;

        if (platformId == MacintoshPlatform)
        {
            var builder = new StringBuilder(bytes.Length);
            foreach (byte value in bytes)
                builder.Append(value < 0x80 ? (char)value : MacRomanHighHalf[value - 0x80]);
            text = builder.ToString();
        }
        else
        {
            //Unicode- and Windows-platform names are UTF-16 big-endian
            text = Encoding.BigEndianUnicode.GetString(bytes);
        }

        return text.Replace("\0", string.Empty).Trim();
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
}
