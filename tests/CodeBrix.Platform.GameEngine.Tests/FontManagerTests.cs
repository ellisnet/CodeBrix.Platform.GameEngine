using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the font manager's stream and byte-array overloads, which let a font that lives inside
/// an archive be registered without first being written to a file.
/// </summary>
public class FontManagerTests : IDisposable
{
    private readonly FontManager _fontManager = FontManager.Instance;
    private readonly List<string> _keys = [];

    /// <summary>Removes only the font keys this fixture registered; the manager is shared.</summary>
    public void Dispose()
    {
        foreach (var key in _keys)
            _fontManager.Remove(key);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void LoadFromBytes_registers_the_typeface_under_the_key()
    {
        //Arrange
        var fontBytes = TryGetFontBytes();

        if (fontBytes is null)
        {
            Assert.Skip("No font data is available on this machine to exercise the happy path.");
            return;
        }

        string key = TrackKey("font_bytes_registers");

        //Act
        var typeface = _fontManager.LoadFromBytes(key, fontBytes);

        //Assert
        typeface.Should().NotBeNull();
        _fontManager.Contains(key).Should().BeTrue();
        _fontManager.Get(key).Should().BeSameAs(typeface);
    }

    [Fact]
    public void LoadFromBytes_replaces_an_existing_key()
    {
        //Arrange
        var fontBytes = TryGetFontBytes();

        if (fontBytes is null)
        {
            Assert.Skip("No font data is available on this machine to exercise the happy path.");
            return;
        }

        string key = TrackKey("font_bytes_replaces");
        var first = _fontManager.LoadFromBytes(key, fontBytes);

        //Act
        var second = _fontManager.LoadFromBytes(key, fontBytes);

        //Assert
        second.Should().NotBeSameAs(first);
        _fontManager.Get(key).Should().BeSameAs(second);
    }

    [Fact]
    public void LoadFromStream_reads_a_forward_only_stream()
    {
        //Arrange - a zip entry stream cannot seek, which is the case this overload exists for.
        var fontBytes = TryGetFontBytes();

        if (fontBytes is null)
        {
            Assert.Skip("No font data is available on this machine to exercise the happy path.");
            return;
        }

        string key = TrackKey("font_stream_forward_only");
        using var stream = new ForwardOnlyStream(fontBytes);

        //Act
        var typeface = _fontManager.LoadFromStream(key, stream);

        //Assert
        typeface.Should().NotBeNull();
        _fontManager.Contains(key).Should().BeTrue();
    }

    [Fact]
    public void LoadFromBytes_rejects_an_empty_key()
    {
        //Arrange
        var data = new byte[] { 1, 2, 3, 4 };

        //Act
        Action nullKey = () => _fontManager.LoadFromBytes(null!, data);
        Action whitespaceKey = () => _fontManager.LoadFromBytes("   ", data);

        //Assert
        nullKey.Should().Throw<ArgumentException>();
        whitespaceKey.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LoadFromBytes_rejects_missing_data()
    {
        //Arrange & Act
        Action nullData = () => _fontManager.LoadFromBytes("font_bytes_null", null!);
        Action emptyData = () => _fontManager.LoadFromBytes("font_bytes_empty", []);

        //Assert
        nullData.Should().Throw<ArgumentNullException>();
        emptyData.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LoadFromBytes_throws_when_the_data_is_not_a_font()
    {
        //Arrange
        var notAFont = Encoding.UTF8.GetBytes("this is not a font file");

        //Act
        Action act = () => _fontManager.LoadFromBytes("font_bytes_garbage", notAFont);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LoadFromStream_rejects_an_empty_key_and_a_missing_stream()
    {
        //Arrange
        using var stream = new MemoryStream([1, 2, 3, 4]);

        //Act
        Action emptyKey = () => _fontManager.LoadFromStream("  ", stream);
        Action nullStream = () => _fontManager.LoadFromStream("font_stream_null", null!);

        //Assert
        emptyKey.Should().Throw<ArgumentException>();
        nullStream.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LoadFromBytes_records_the_family_name_from_the_font_data()
    {
        //Arrange
        var fontBytes = TryGetFontBytes();

        if (fontBytes is null)
        {
            Assert.Skip("No font data is available on this machine to exercise the happy path.");
            return;
        }

        string key = TrackKey("font_family_recorded");

        //Act
        var typeface = _fontManager.LoadFromBytes(key, fontBytes);

        //Assert - read from the font's own name table, so it matches a direct read of that table
        string familyName = _fontManager.GetFamilyName(key);
        familyName.Should().Be(FontFamilyNameReader.GetFamilyName(typeface));
        familyName.Should().NotBeNullOrWhiteSpace();
        _fontManager.TryGetFamilyName(key, out var tried).Should().BeTrue();
        tried.Should().Be(familyName);
    }

    [Fact]
    public void Family_name_lookups_find_the_font_by_its_family_name()
    {
        //Arrange
        var fontBytes = TryGetFontBytes();

        if (fontBytes is null)
        {
            Assert.Skip("No font data is available on this machine to exercise the happy path.");
            return;
        }

        string firstKey = TrackKey("font_family_lookup_b");
        string secondKey = TrackKey("font_family_lookup_a");
        _fontManager.LoadFromBytes(firstKey, fontBytes);
        _fontManager.LoadFromBytes(secondKey, fontBytes);
        string familyName = _fontManager.GetFamilyName(firstKey);

        //Act
        var keys = _fontManager.GetKeysByFamilyName(familyName.ToUpperInvariant());
        bool found = _fontManager.TryGetByFamilyName(familyName, out var typeface);

        //Assert - both keys match, ignoring case, in key order, and the first key in order wins
        keys.Should().ContainInOrder(secondKey, firstKey);
        found.Should().BeTrue();
        typeface.Should().BeSameAs(_fontManager.Get(keys[0]));
    }

    [Fact]
    public void Remove_forgets_the_family_name()
    {
        //Arrange
        var fontBytes = TryGetFontBytes();

        if (fontBytes is null)
        {
            Assert.Skip("No font data is available on this machine to exercise the happy path.");
            return;
        }

        string key = TrackKey("font_family_removed");
        _fontManager.LoadFromBytes(key, fontBytes);
        string loadedFamilyName = _fontManager.GetFamilyName(key);

        //Act
        _fontManager.Remove(key);

        //Assert
        _fontManager.TryGetFamilyName(key, out var familyName).Should().BeFalse();
        familyName.Should().BeNull();
        _fontManager.GetKeysByFamilyName(loadedFamilyName).Should().NotContain(key);
        Action get = () => _fontManager.GetFamilyName(key);
        get.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Family_name_lookups_reject_blank_input()
    {
        //Act
        Action getBlank = () => _fontManager.GetFamilyName("  ");

        //Assert
        getBlank.Should().Throw<ArgumentException>();
        _fontManager.TryGetFamilyName("  ", out _).Should().BeFalse();
        _fontManager.GetKeysByFamilyName("  ").Should().BeEmpty();
        _fontManager.TryGetByFamilyName("  ", out var typeface).Should().BeFalse();
        typeface.Should().BeNull();
        _fontManager.TryGetByFamilyName("No Such Family 7f3c", out _).Should().BeFalse();
    }

    private string TrackKey(string key)
    {
        _keys.Add(key);

        return key;
    }

    /// <summary>
    /// Returns the bytes of a real font file, or <see langword="null"/> when this machine offers
    /// no font whose data can be read back.
    /// </summary>
    private static byte[]? TryGetFontBytes()
    {
        try
        {
            var typeface = SKTypeface.Default;
            using var fontStream = typeface.OpenStream(out _);

            if (fontStream is null || fontStream.Length <= 0)
                return null;

            using var data = SKData.Create(fontStream);
            var bytes = data?.ToArray();

            return bytes is { Length: > 0 } ? bytes : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A read-only stream that cannot seek or report its length, standing in for an archive entry.
    /// </summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();

            base.Dispose(disposing);
        }
    }
}
