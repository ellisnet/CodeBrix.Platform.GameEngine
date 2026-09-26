using CodeBrix.Platform.GameEngine.Assets.Providers;

namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// What one asset key resolved to in a provider's catalog, found without materializing anything.
/// </summary>
/// <param name="Key">The key as it was asked for.</param>
/// <param name="Found">Whether the provider holds an asset with that key.</param>
/// <param name="Kind">The asset's kind; <see cref="GameAssetKind.Unknown"/> when it was not found.</param>
/// <param name="SizeBytes">The size of the asset's file inside its pack; 0 when it was not found.</param>
/// <param name="AtlasFrameCount">The number of frames a sprite atlas declares; 0 for anything else.</param>
public sealed record KenneyKeyStatus(string Key, bool Found, GameAssetKind Kind, long SizeBytes, int AtlasFrameCount);
