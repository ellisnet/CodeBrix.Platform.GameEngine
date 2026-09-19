using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the rule that turns a font's OpenType <c>name</c> table into the platform-neutral family name
/// the font manager reports, using hand-built tables so every branch is reached without a font file.
/// </summary>
public class FontFamilyNameReaderTests
{
    private const ushort Unicode = 0;
    private const ushort Macintosh = 1;
    private const ushort Windows = 3;
    private const ushort UsEnglish = 0x0409;
    private const ushort French = 0x040C;

    [Fact]
    public void ReadFamilyName_reads_the_family_name_when_there_is_no_typographic_family()
    {
        //Arrange - how "Kenney Future Narrow" is declared: name ID 1 only, which DirectWrite would
        //  otherwise report as "Kenney Future"
        byte[] table = NameTable(
            Record(Macintosh, 0, 0, 1, "Kenney Future Narrow"),
            Record(Windows, 1, UsEnglish, 1, "Kenney Future Narrow"),
            Record(Windows, 1, UsEnglish, 2, "Regular"),
            Record(Windows, 1, UsEnglish, 4, "Kenney Future Narrow Regular"));

        //Act
        string? familyName = FontFamilyNameReader.ReadFamilyName(table);

        //Assert
        familyName.Should().Be("Kenney Future Narrow");
    }

    [Fact]
    public void ReadFamilyName_prefers_the_typographic_family()
    {
        //Arrange
        byte[] table = NameTable(
            Record(Windows, 1, UsEnglish, 1, "Open Sans SemiBold"),
            Record(Windows, 1, UsEnglish, 16, "Open Sans"));

        //Act
        string? familyName = FontFamilyNameReader.ReadFamilyName(table);

        //Assert
        familyName.Should().Be("Open Sans");
    }

    [Fact]
    public void ReadFamilyName_prefers_windows_us_english_over_every_other_record()
    {
        //Arrange - the preferred record is deliberately last
        byte[] table = NameTable(
            Record(Macintosh, 0, 0, 1, "Mac Name"),
            Record(Windows, 1, French, 1, "Nom Francais"),
            Record(Unicode, 3, 0, 1, "Unicode Name"),
            Record(Windows, 1, UsEnglish, 1, "Windows Name"));

        //Act
        string? familyName = FontFamilyNameReader.ReadFamilyName(table);

        //Assert
        familyName.Should().Be("Windows Name");
    }

    [Fact]
    public void ReadFamilyName_falls_back_through_unicode_windows_and_macintosh_records()
    {
        //Arrange
        byte[] unicodeAndMore = NameTable(
            Record(Macintosh, 0, 0, 1, "Mac Name"),
            Record(Windows, 1, French, 1, "Nom Francais"),
            Record(Unicode, 3, 0, 1, "Unicode Name"));
        byte[] windowsAndMac = NameTable(
            Record(Macintosh, 0, 0, 1, "Mac Name"),
            Record(Windows, 1, French, 1, "Nom Francais"));
        byte[] macOnly = NameTable(Record(Macintosh, 0, 0, 1, "Mac Name"));

        //Act & Assert
        FontFamilyNameReader.ReadFamilyName(unicodeAndMore).Should().Be("Unicode Name");
        FontFamilyNameReader.ReadFamilyName(windowsAndMac).Should().Be("Nom Francais");
        FontFamilyNameReader.ReadFamilyName(macOnly).Should().Be("Mac Name");
    }

    [Fact]
    public void ReadFamilyName_decodes_macintosh_roman()
    {
        //Arrange - 0x8E is é and 0xA9 is © in Macintosh Roman
        byte[] table = NameTable(RawRecord(Macintosh, 0, 0, 1, [(byte)'C', 0x8E, (byte)'z', (byte)' ', 0xA9]));

        //Act
        string? familyName = FontFamilyNameReader.ReadFamilyName(table);

        //Assert
        familyName.Should().Be("Céz ©");
        FontFamilyNameReader.MacRomanHighHalf.Length.Should().Be(128);
    }

    [Fact]
    public void ReadFamilyName_skips_blank_names_and_unreadable_encodings()
    {
        //Arrange - a blank typographic family, and a Windows Shift-JIS record, are both ignored
        byte[] table = NameTable(
            Record(Windows, 1, UsEnglish, 16, "   "),
            RawRecord(Windows, 2, UsEnglish, 1, [0x82, 0xA0]),
            Record(Windows, 1, French, 1, "Nom"));

        //Act
        string? familyName = FontFamilyNameReader.ReadFamilyName(table);

        //Assert
        familyName.Should().Be("Nom");
    }

    [Fact]
    public void ReadFamilyName_returns_null_for_a_table_without_a_family_name()
    {
        //Arrange
        byte[] noFamily = NameTable(Record(Windows, 1, UsEnglish, 4, "Full Name"));
        byte[] truncated = [0, 0, 0, 5];

        //Act & Assert
        FontFamilyNameReader.ReadFamilyName(noFamily).Should().BeNull();
        FontFamilyNameReader.ReadFamilyName(truncated).Should().BeNull();
        FontFamilyNameReader.ReadFamilyName([]).Should().BeNull();
    }

    [Fact]
    public void ReadFamilyName_ignores_a_record_that_points_past_the_table()
    {
        //Arrange
        byte[] table = NameTable(
            Record(Windows, 1, UsEnglish, 1, "Good"),
            Record(Windows, 1, UsEnglish, 16, "Truncated"));
        int lengthOfTruncated = Encoding.BigEndianUnicode.GetByteCount("Truncated");
        byte[] cut = table[..^(lengthOfTruncated / 2)];

        //Act
        string? familyName = FontFamilyNameReader.ReadFamilyName(cut);

        //Assert
        familyName.Should().Be("Good");
    }

    private static (ushort Platform, ushort Encoding, ushort Language, ushort NameId, byte[] Bytes) Record(
        ushort platform, ushort encoding, ushort language, ushort nameId, string text)
    {
        byte[] bytes = platform == Macintosh
            ? Encoding.ASCII.GetBytes(text)
            : Encoding.BigEndianUnicode.GetBytes(text);

        return (platform, encoding, language, nameId, bytes);
    }

    private static (ushort Platform, ushort Encoding, ushort Language, ushort NameId, byte[] Bytes) RawRecord(
        ushort platform, ushort encoding, ushort language, ushort nameId, byte[] bytes) =>
        (platform, encoding, language, nameId, bytes);

    /// <summary>Builds a format 0 <c>name</c> table holding the given records, in order.</summary>
    private static byte[] NameTable(
        params (ushort Platform, ushort Encoding, ushort Language, ushort NameId, byte[] Bytes)[] records)
    {
        using var output = new MemoryStream();
        var storage = new List<byte>();

        WriteUInt16(output, 0);
        WriteUInt16(output, (ushort)records.Length);
        WriteUInt16(output, (ushort)(6 + (records.Length * 12)));

        foreach (var record in records)
        {
            WriteUInt16(output, record.Platform);
            WriteUInt16(output, record.Encoding);
            WriteUInt16(output, record.Language);
            WriteUInt16(output, record.NameId);
            WriteUInt16(output, (ushort)record.Bytes.Length);
            WriteUInt16(output, (ushort)storage.Count);
            storage.AddRange(record.Bytes);
        }

        output.Write(storage.ToArray());
        return output.ToArray();
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
