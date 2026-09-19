using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Opens fixture bundles, builds synthetic ones, and hands out the catalogued entries the model
/// tests materialize from - then closes and deletes everything it opened or wrote.
/// </summary>
internal sealed class TestModelSources : IDisposable
{
    private readonly List<string> _scratchFolders = [];
    private readonly List<KenneyAssetSource> _sources = [];

    /// <summary>
    /// Opens a bundle and indexes it.
    /// </summary>
    /// <param name="bundleFileNameOrPath">A fixture bundle's file name, or the full path of a bundle
    /// or extracted folder.</param>
    /// <returns>The index.</returns>
    public KenneyPackIndex Index(string bundleFileNameOrPath)
    {
        KenneyPackIndex index = new();
        index.AddSource(Open(bundleFileNameOrPath));

        return index;
    }

    /// <summary>
    /// Opens a bundle, indexes it, and finds one catalogued asset.
    /// </summary>
    /// <param name="bundleFileNameOrPath">A fixture bundle's file name, or the full path of a bundle
    /// or extracted folder.</param>
    /// <param name="key">The asset's key, with or without the provider prefix.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the bundle holds no such asset, which
    /// means the test's expectation of the fixture is wrong.</exception>
    public KenneyAssetEntry Entry(string bundleFileNameOrPath, string key)
    {
        KenneyPackIndex index = Index(bundleFileNameOrPath);

        return Entry(index, key);
    }

    /// <summary>
    /// Finds one catalogued asset of an index already opened.
    /// </summary>
    /// <param name="index">The index to look in.</param>
    /// <param name="key">The asset's key, with or without the provider prefix.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the index holds no such asset.</exception>
    public static KenneyAssetEntry Entry(KenneyPackIndex index, string key)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (!index.TryGetEntry(key, out KenneyAssetEntry? entry) || entry is null)
        {
            throw new InvalidOperationException($"The fixture holds no asset keyed '{key}'.");
        }

        return entry;
    }

    /// <summary>
    /// Opens a bundle or extracted folder as a source, and keeps it for disposal.
    /// </summary>
    /// <param name="bundleFileNameOrPath">A fixture bundle's file name, or the full path of a bundle
    /// or extracted folder.</param>
    /// <returns>The source.</returns>
    public KenneyAssetSource Open(string bundleFileNameOrPath)
    {
        string path = Path.IsPathRooted(bundleFileNameOrPath)
            ? bundleFileNameOrPath
            : TestFixtures.BundlePath(bundleFileNameOrPath);

        KenneyAssetSource source = KenneyAssetSource.Open(path);
        _sources.Add(source);

        return source;
    }

    /// <summary>
    /// Writes a synthetic bundle into a scratch folder, for the cases no real bundle covers.
    /// </summary>
    /// <param name="purpose">A short word naming what the bundle is for, used in its file name.</param>
    /// <param name="entries">Each entry's archive path mapped to its bytes.</param>
    /// <returns>The full path of the written zip file.</returns>
    public string BuildBundle(string purpose, IReadOnlyDictionary<string, byte[]> entries)
    {
        string folder = TestFixtures.CreateScratchFolder(purpose);
        _scratchFolders.Add(folder);

        string zipPath = Path.Combine(folder, $"kenney_{purpose}.zip");
        TestFixtures.BuildZip(zipPath, entries);

        return zipPath;
    }

    /// <summary>
    /// Extracts a fixture bundle into a scratch folder, so the same asset can be read from a folder
    /// as well as from a zip.
    /// </summary>
    /// <param name="fileName">The fixture bundle's file name.</param>
    /// <param name="purpose">A short word naming what the folder is for, used in its name.</param>
    /// <returns>The full path of the folder.</returns>
    public string Extract(string fileName, string purpose)
    {
        string folder = TestFixtures.ExtractBundle(fileName, purpose);
        _scratchFolders.Add(folder);

        return folder;
    }

    /// <summary>
    /// Reads one file out of a fixture bundle, for tests that repackage real model bytes.
    /// </summary>
    /// <param name="bundleFileName">The fixture bundle's file name.</param>
    /// <param name="archivePath">The file's path inside the bundle.</param>
    /// <returns>The file's bytes.</returns>
    public byte[] ReadFromBundle(string bundleFileName, string archivePath)
    {
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(bundleFileName));

        return archive.ReadBytes(archivePath);
    }

    /// <summary>
    /// Closes every source and deletes every scratch folder this helper created.
    /// </summary>
    public void Dispose()
    {
        foreach (KenneyAssetSource source in _sources) { source.Dispose(); }

        foreach (string folder in _scratchFolders) { TestFixtures.DeleteScratchFolder(folder); }
    }
}
