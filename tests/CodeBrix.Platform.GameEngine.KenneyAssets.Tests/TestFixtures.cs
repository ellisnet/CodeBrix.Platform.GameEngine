using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// The real Kenney bundles the tests read, and the scratch folders they build things in.
/// </summary>
internal static class TestFixtures
{
    /// <summary>A 2D art pack: two correct sprite atlases plus PNG/Default and PNG/Double.</summary>
    public const string PuzzlePackFileName = "kenney_puzzle-pack-1.zip";

    /// <summary>An audio pack: 73 .ogg files and a stray desktop.ini.</summary>
    public const string SciFiSoundsFileName = "kenney_sci-fi-sounds.zip";

    /// <summary>A 3D model pack: the same models as .fbx, .glb and .obj, with .mtl materials.</summary>
    public const string BlockyCharactersFileName = "kenney_blocky-characters_20.zip";

    /// <summary>A larger 3D model pack, holding three different Textures/colormap.png files.</summary>
    public const string BrickKitFileName = "kenney_brick-kit.zip";

    /// <summary>A mixed bundle: audio, fonts, images, vectors, glTF models and a Tiled map.</summary>
    public const string SimulatedBundleFileName = "simulated_bundle.zip";

    /// <summary>
    /// Gets the full path of a fixture bundle copied beside the test assembly.
    /// </summary>
    /// <param name="fileName">The bundle's file name, from the constants on this class.</param>
    /// <returns>The full path of the bundle.</returns>
    public static string BundlePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);

    /// <summary>
    /// Creates an empty scratch folder under the test output directory and returns its path. The test
    /// output directory is used rather than the system temporary folder, which is memory-backed here.
    /// </summary>
    /// <param name="purpose">A short word naming what the folder is for, used in its name.</param>
    /// <returns>The full path of the new folder.</returns>
    public static string CreateScratchFolder(string purpose)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "test-temp", $"{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes a scratch folder and everything in it, ignoring a folder that is already gone.
    /// </summary>
    /// <param name="path">The folder to delete.</param>
    public static void DeleteScratchFolder(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return; }

        try
        {
            if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); }
        }
        catch (IOException)
        {
            //A leftover scratch folder is not worth failing a test over
        }
    }

    /// <summary>
    /// Extracts a fixture bundle into a fresh scratch folder.
    /// </summary>
    /// <param name="fileName">The bundle's file name, from the constants on this class.</param>
    /// <param name="purpose">A short word naming what the folder is for, used in its name.</param>
    /// <returns>The full path of the folder the bundle was extracted into.</returns>
    public static string ExtractBundle(string fileName, string purpose)
    {
        string folder = CreateScratchFolder(purpose);
        ZipFile.ExtractToDirectory(BundlePath(fileName), folder);
        return folder;
    }

    /// <summary>
    /// Writes a zip file holding exactly the given entries, for the cases no real bundle covers.
    /// </summary>
    /// <param name="zipPath">The full path of the zip file to write.</param>
    /// <param name="entries">Each entry's archive path mapped to its bytes.</param>
    public static void BuildZip(string zipPath, IReadOnlyDictionary<string, byte[]> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using FileStream file = File.Create(zipPath);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);
        foreach ((string path, byte[] bytes) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>
    /// Writes a folder holding exactly the given files, for the folder-archive cases.
    /// </summary>
    /// <param name="folderPath">The folder to write into; it is created when absent.</param>
    /// <param name="entries">Each file's folder-relative path mapped to its bytes.</param>
    public static void BuildFolder(string folderPath, IReadOnlyDictionary<string, byte[]> entries)
    {
        foreach ((string relativePath, byte[] bytes) in entries)
        {
            string fullPath = Path.Combine(folderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
        }
    }

    /// <summary>
    /// A Kenney licence file's text, in the shape every pack ships: a blank first line, then the title
    /// and version, then the CC0 notice.
    /// </summary>
    /// <param name="title">The pack title.</param>
    /// <param name="version">The pack version.</param>
    /// <returns>The licence text.</returns>
    public static string LicenseText(string title, string version) =>
        $"\t\r\n\r\n\t{title} ({version})\r\n\r\n\tCreated/distributed by Kenney (www.kenney.nl)\r\n" +
        "\r\n\tLicense: (Creative Commons Zero, CC0)\r\n\thttp://creativecommons.org/publicdomain/zero/1.0/\r\n";

    /// <summary>
    /// A few bytes standing in for an image. Nothing under test decodes an image, so the content only
    /// has to exist.
    /// </summary>
    /// <returns>The stand-in bytes.</returns>
    public static byte[] FakeImage() => [0x89, 0x50, 0x4E, 0x47];
}
