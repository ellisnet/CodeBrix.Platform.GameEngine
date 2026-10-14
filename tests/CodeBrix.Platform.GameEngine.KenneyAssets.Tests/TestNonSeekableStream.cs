using System;
using System.IO;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// A read-only, forward-only stream over some bytes, standing in for a packaged app file opened as a
/// stream: it forwards reads, reports that it cannot seek, refuses every seek, position and length
/// request, and records whether it was read from and whether it was disposed.
/// </summary>
internal sealed class TestNonSeekableStream : Stream
{
    private readonly Stream _inner;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestNonSeekableStream"/> class over the given bytes.
    /// </summary>
    /// <param name="bytes">The bytes the stream hands out, from the first.</param>
    public TestNonSeekableStream(byte[] bytes)
    {
        _inner = new MemoryStream(bytes, writable: false);
    }

    /// <summary>
    /// Creates a stream over the bytes of a fixture bundle.
    /// </summary>
    /// <param name="fileName">The bundle's file name, from the constants on <see cref="TestFixtures"/>.</param>
    /// <returns>The new stream.</returns>
    public static TestNonSeekableStream OverBundle(string fileName) =>
        new(File.ReadAllBytes(TestFixtures.BundlePath(fileName)));

    /// <summary>
    /// Gets a value indicating whether the stream has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Gets a value indicating whether anything has been read from the stream.
    /// </summary>
    public bool WasRead { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => !_disposed;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException("This stream cannot report its length.");

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException("This stream cannot report its position.");
        set => throw new NotSupportedException("This stream cannot seek.");
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WasRead = true;
        return _inner.Read(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("This stream cannot seek.");

    /// <inheritdoc/>
    public override void SetLength(long value) =>
        throw new NotSupportedException("This stream cannot be resized.");

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("This stream cannot be written.");

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
