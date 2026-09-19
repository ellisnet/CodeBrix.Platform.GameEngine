using System;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

/// <summary>
/// Thrown when a Tiled document cannot be parsed, or uses a feature this library does not support.
/// </summary>
/// <remarks>
/// The message always says what the document did and what is supported instead, because "the map did
/// not load" is not something a game developer can act on. A caller that would rather branch than
/// catch can use the <c>TryParse</c> methods of <see cref="TiledMapParser"/>, which hand back the same
/// message as a string.
/// </remarks>
public sealed class TiledMapParseException : FormatException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TiledMapParseException"/> class.
    /// </summary>
    /// <param name="message">The message that describes what could not be parsed.</param>
    public TiledMapParseException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="TiledMapParseException"/> class with the document
    /// that failed.
    /// </summary>
    /// <param name="message">The message that describes what could not be parsed.</param>
    /// <param name="documentPath">The archive path of the document, or <see langword="null"/> when it is unknown.</param>
    public TiledMapParseException(string message, string? documentPath)
        : base(message)
    {
        DocumentPath = documentPath;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TiledMapParseException"/> class with the document
    /// that failed and the underlying failure.
    /// </summary>
    /// <param name="message">The message that describes what could not be parsed.</param>
    /// <param name="documentPath">The archive path of the document, or <see langword="null"/> when it is unknown.</param>
    /// <param name="innerException">The exception that caused this one, such as an XML syntax error.</param>
    public TiledMapParseException(string message, string? documentPath, Exception innerException)
        : base(message, innerException)
    {
        DocumentPath = documentPath;
    }

    /// <summary>
    /// Gets the archive path of the document that could not be parsed, or <see langword="null"/> when
    /// it is unknown.
    /// </summary>
    public string? DocumentPath { get; }
}
