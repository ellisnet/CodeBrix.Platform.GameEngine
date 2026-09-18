using System;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Thrown when an asset exists in a provider's catalog but the engine cannot turn it into a
/// runtime object, either because its <see cref="GameAssetKind"/> has no engine representation
/// or because the owning provider does not implement the matching capability interface.
/// </summary>
public class UnsupportedGameAssetException : InvalidOperationException
{
    /// <summary>
    /// The message used when no asset kind or key is available.
    /// </summary>
    public const string DefaultMessage = "This type of asset is not supported at this time.";

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedGameAssetException"/> class with
    /// the default message.
    /// </summary>
    public UnsupportedGameAssetException()
        : base(DefaultMessage) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedGameAssetException"/> class with a
    /// specified message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public UnsupportedGameAssetException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedGameAssetException"/> class with a
    /// specified message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public UnsupportedGameAssetException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedGameAssetException"/> class for a
    /// specific asset, composing the message from the asset kind and key.
    /// </summary>
    /// <param name="kind">The kind of the asset that could not be materialized.</param>
    /// <param name="key">The namespaced key of the asset, when one is known.</param>
    /// <remarks>
    /// The composed message always begins with <see cref="DefaultMessage"/>, so callers can match
    /// on that sentence regardless of which constructor produced the exception.
    /// </remarks>
    public UnsupportedGameAssetException(GameAssetKind kind, string? key)
        : base(ComposeMessage(kind, key))
    {
        Kind = kind;
        Key = key;
    }

    /// <summary>
    /// Gets the kind of the asset that could not be materialized.
    /// </summary>
    public GameAssetKind Kind { get; } = GameAssetKind.Unknown;

    /// <summary>
    /// Gets the namespaced key of the asset that could not be materialized, or
    /// <see langword="null"/> when no key was available.
    /// </summary>
    public string? Key { get; }

    private static string ComposeMessage(GameAssetKind kind, string? key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? $"{DefaultMessage} (asset kind: {kind})"
            : $"{DefaultMessage} (asset kind: {kind}; key: '{key}')";
    }
}
