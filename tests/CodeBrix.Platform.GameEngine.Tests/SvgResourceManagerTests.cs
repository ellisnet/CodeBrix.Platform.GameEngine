using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.Platform.GameEngine.Drawing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers registering an SVG resource that was read from a stream rather than a file path.
/// </summary>
public class SvgResourceManagerTests : IDisposable
{
    private const string BlueCircleSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
          <circle cx="16" cy="16" r="15" fill="#0000ff" />
        </svg>
        """;

    private readonly SvgResourceManager _manager = SvgResourceManager.Instance;
    private readonly List<string> _keys = [];

    /// <summary>Unloads only the keys this fixture registered; the manager is shared.</summary>
    public void Dispose()
    {
        foreach (var key in _keys)
            _manager.Unload(key);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void LoadFromStream_registers_the_resource_under_the_key()
    {
        //Arrange
        string key = TrackKey("svg_stream_registers");
        using var stream = SvgStream(BlueCircleSvg);

        //Act
        var resource = _manager.LoadFromStream(key, stream);

        //Assert
        resource.Should().NotBeNull();
        resource.IntrinsicSize.Width.Should().Be(32f);
        _manager.Contains(key).Should().BeTrue();
        _manager.Get(key).Should().BeSameAs(resource);
    }

    [Fact]
    public void LoadFromStream_replaces_an_existing_key()
    {
        //Arrange
        string key = TrackKey("svg_stream_replaces");
        using var firstStream = SvgStream(BlueCircleSvg);
        using var secondStream = SvgStream(BlueCircleSvg);
        var first = _manager.LoadFromStream(key, firstStream);

        //Act
        var second = _manager.LoadFromStream(key, secondStream);

        //Assert
        second.Should().NotBeSameAs(first);
        _manager.Get(key).Should().BeSameAs(second);
    }

    [Fact]
    public void LoadFromStream_rejects_an_empty_key_and_a_missing_stream()
    {
        //Arrange
        using var stream = SvgStream(BlueCircleSvg);

        //Act
        Action emptyKey = () => _manager.LoadFromStream("   ", stream);
        Action nullStream = () => _manager.LoadFromStream("svg_stream_null", null!);

        //Assert
        emptyKey.Should().Throw<ArgumentException>();
        nullStream.Should().Throw<ArgumentNullException>();
    }

    private string TrackKey(string key)
    {
        _keys.Add(key);

        return key;
    }

    private static MemoryStream SvgStream(string svg)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(svg), writable: false);
    }
}
