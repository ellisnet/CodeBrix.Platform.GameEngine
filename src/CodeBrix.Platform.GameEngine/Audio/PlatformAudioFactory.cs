using Microsoft.Extensions.Logging;
using CodeBrix.Audio.Wave; //was previously: using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Audio; //was previously: Gondwana.Audio;
/// <summary>
/// Resolves an audio file extension to the reader that decodes it, for every engine path that
/// loads a sound by name — <see cref="AudioResourceManager"/>, <see cref="CachedSound.FromFile"/>,
/// and <see cref="SoundChannel"/> clips.
/// </summary>
/// <remarks>
/// <para>
/// WAV, MP3, Ogg Vorbis and FLAC work out of the box, and every one of them is fully managed, so a
/// game's assets never need converting for a particular target.
/// </para>
/// <para>
/// Ogg Vorbis matters for game assets specifically: free asset packs ship .ogg almost exclusively
/// (the Kenney All-in-1 bundle, for one, is 1,342 .ogg files and nothing else), so without it those
/// packs cannot be loaded at all.
/// </para>
/// <para>
/// ANY OTHER FORMAT REGISTERED WITH CodeBrix.Audio WORKS HERE TOO, with no engine change. This type
/// resolves in two steps: first its own table (what <see cref="Register"/> adds), then
/// CodeBrix.Audio's <see cref="AudioFileReaderRegistry"/>. So a codec that ships as a separate
/// package — Opus, which is BSD-3-Clause and therefore cannot be a dependency of this MIT-licensed
/// engine — becomes a first-class engine format the moment the application registers it:
/// </para>
/// <code>
/// CodeBrixAudioOpus.Register();          // once, at start-up, from the application
/// // ...every engine audio path now loads .opus exactly like .ogg
/// </code>
/// <para>
/// The engine's own table takes precedence, so <see cref="Register"/> can override a built-in
/// reader (or supply the <c>requiresFile</c> behavior the CodeBrix.Audio registry has no concept
/// of) without the registry overriding it back.
/// </para>
/// </remarks>
public static class PlatformAudioFactory
{
    private static readonly Dictionary<string, (Func<Stream, WaveStream> readerFactory, bool requiresFile)> _readers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a reader factory for a specific audio file format, taking precedence over the
    /// format's reader in CodeBrix.Audio's <see cref="AudioFileReaderRegistry"/>.
    /// </summary>
    /// <remarks>If a reader for the specified extension already exists, it will be replaced with the new factory.
    /// The extension comparison is case-insensitive. To add a format for EVERY CodeBrix.Audio consumer rather
    /// than for the engine alone, register it with <see cref="AudioFileReaderRegistry"/> instead — this type
    /// falls through to it, so nothing further is needed here.</remarks>
    /// <param name="extension">The file extension to register (e.g., ".wav", ".mp3"). Can be with or without the leading dot.</param>
    /// <param name="readerFactory">A factory function that creates a <see cref="WaveStream"/> from an input stream.</param>
    /// <param name="requiresFile">A value indicating whether the reader requires a physical file on disk rather than a stream. Defaults to <see langword="false"/>.</param>
    public static void Register(string extension, Func<Stream, WaveStream> readerFactory, bool requiresFile = false)
    {
        Engine.Logger.LogInformation("Registering audio reader for extension: {Extension}", extension);
        _readers[NormalizeExt(extension)] = (readerFactory, requiresFile);
    }

    /// <summary>
    /// Determines whether the specified audio format is supported — by this type's own registrations
    /// or by CodeBrix.Audio's <see cref="AudioFileReaderRegistry"/>.
    /// </summary>
    /// <param name="fileNameOrExt">A file name or file extension to check for support (e.g., "audio.mp3" or ".mp3").</param>
    /// <returns><see langword="true"/> if the format is supported; otherwise, <see langword="false"/>.</returns>
    public static bool Supports(string fileNameOrExt)
    {
        var ext = NormalizeExt(Path.GetExtension(fileNameOrExt));
        return _readers.ContainsKey(ext) || AudioFileReaderRegistry.Supports(ext);
    }

    /// <summary>
    /// Gets a collection of all supported audio file extensions — this type's own registrations
    /// combined with CodeBrix.Audio's <see cref="AudioFileReaderRegistry"/>.
    /// </summary>
    /// <returns>An enumerable collection of supported file extensions in alphabetical order, including the leading dot (e.g., ".mp3", ".wav").</returns>
    public static IEnumerable<string> SupportedExtensions()
    {
        return _readers.Keys
            .Concat(AudioFileReaderRegistry.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ext => ext, StringComparer.Ordinal);
    }

    internal static (Func<Stream, WaveStream> factory, bool requiresFile) GetReaderFactory(string fileNameOrExt)
    {
        var ext = NormalizeExt(Path.GetExtension(fileNameOrExt));

        // The engine's own registrations win, so a game can override a built-in reader or declare
        // that one needs a real file on disk (which the CodeBrix.Audio registry cannot express).
        if (_readers.TryGetValue(ext, out var entry))
        {
            return entry;
        }

        // Then CodeBrix.Audio's registry, which is where the four built-in formats live and where a
        // separately licensed codec package (Opus) registers itself. Nothing reached through here
        // needs a file on disk: a registry factory takes a stream by contract.
        if (AudioFileReaderRegistry.Supports(ext))
        {
            return (AudioFileReaderRegistry.GetFactory(ext), false);
        }

        Engine.Logger.LogError("Unsupported audio format: {Extension}", ext);
        throw new NotSupportedException(BuildUnsupportedMessage(ext));
    }

    /// <summary>
    /// Builds the "unsupported format" message. A developer who hits this should be told what to do
    /// about it, not just what failed — and for .opus, which is a deliberate packaging decision
    /// rather than a missing feature, that means naming the exact call that fixes it.
    /// </summary>
    private static string BuildUnsupportedMessage(string ext)
    {
        var supported = string.Join(", ", SupportedExtensions());

        if (string.Equals(ext, ".opus", StringComparison.OrdinalIgnoreCase))
        {
            return $"Format '{ext}' is not registered. Opus ships as a separate package because it is "
                 + "BSD-3-Clause and this engine is MIT: add the CodeBrix.Audio.Opus.BsdLicenseForever "
                 + "package to your APPLICATION and call CodeBrixAudioOpus.Register() once at start-up. "
                 + $"Registered formats: {supported}.";
        }

        return $"Format '{ext}' is not supported on this platform. Registered formats: {supported}. "
             + "Add one with AudioFileReaderRegistry.Register (all CodeBrix.Audio consumers) or "
             + "PlatformAudioFactory.Register (this engine only).";
    }

    internal static string NormalizeExt(string ext)
        => ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
}
