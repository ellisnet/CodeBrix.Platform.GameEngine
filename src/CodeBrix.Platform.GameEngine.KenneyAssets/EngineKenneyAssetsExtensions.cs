using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// Adds Kenney asset bundle support to an <see cref="Engine"/> instance.
/// </summary>
/// <remarks>
/// This is the entry point for the package: everything else can be ignored by a game that just wants
/// to load the assets out of the Kenney bundles it shipped.
/// </remarks>
public static class EngineKenneyAssetsExtensions
{
    /// <summary>
    /// Registers Kenney asset bundles with the engine and returns the provider that serves them.
    /// </summary>
    /// <param name="engine">The engine to register the assets with.</param>
    /// <param name="zipFilesOrFolders">
    /// The paths of the Kenney bundles (.zip files) and extracted bundle folders to register.
    /// </param>
    /// <returns>
    /// The registered provider, which is the one already serving
    /// <see cref="KenneyGameAssetProvider.DefaultProviderId"/> when there is one.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="engine"/> or <paramref name="zipFilesOrFolders"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another kind of asset provider is already registered under that identifier.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Call it once, with the bundles the game ships; call it again later - for downloadable content or
    /// a mod folder - and the new bundles are ADDED to the same provider. A source that cannot be read
    /// costs the game those assets, not its start-up: the reason is written to the engine log and kept
    /// in <see cref="KenneyGameAssetProvider.Warnings"/>.
    /// </para>
    /// <para>
    /// This is the short form, which takes the defaults: the provider is called
    /// <c>kenney</c>, a source folder is taken as one pack, and an unreadable source is a warning. The
    /// overload taking <see cref="KenneyAssetsOptions"/> changes any of those.
    /// </para>
    /// </remarks>
    public static KenneyGameAssetProvider UseKenneyAssets(
        this Engine engine, params string[] zipFilesOrFolders)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(zipFilesOrFolders);

        return engine.UseKenneyAssets(new KenneyAssetsOptions { Sources = zipFilesOrFolders });
    }

    /// <summary>
    /// Registers Kenney asset bundles with the engine, with the settings the options carry, and returns
    /// the provider that serves them.
    /// </summary>
    /// <param name="engine">The engine to register the assets with.</param>
    /// <param name="options">
    /// The sources to register, the identifier to namespace their keys with, whether a source folder
    /// holding pack folders is searched, and whether an unreadable source is a warning or an error.
    /// </param>
    /// <returns>
    /// The registered provider, which is the one already serving
    /// <see cref="KenneyAssetsOptions.ProviderId"/> when there is one.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="engine"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="KenneyAssetsOptions.ProviderId"/> is null, empty, whitespace or contains
    /// a colon; and, when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off, when
    /// a source path is blank.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another kind of asset provider is already registered under that identifier.
    /// </exception>
    /// <exception cref="System.IO.FileNotFoundException">
    /// Thrown when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off and no file
    /// or folder exists at a source path.
    /// </exception>
    /// <exception cref="System.IO.IOException">
    /// Thrown when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off and a source
    /// zip file cannot be read.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Registration is explicit and idempotent, like the engine's other opt-in subsystems: nothing
    /// happens until this is called, and calling it again with the same identifier adds the new sources
    /// to the provider that is already serving the game rather than replacing it. Two sets of assets
    /// that must stay apart take one <see cref="KenneyAssetsOptions.ProviderId"/> each.
    /// </para>
    /// <para>
    /// The provider is registered with <c>Engine.Managers.AssetProviders</c> and is disposed when the
    /// engine shuts down. It does not become part of saved engine state, so a game registers its
    /// bundles on every run.
    /// </para>
    /// </remarks>
    public static KenneyGameAssetProvider UseKenneyAssets(this Engine engine, KenneyAssetsOptions options)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);

        GameAssetProviderRegistry registry = engine.Managers.AssetProviders;
        KenneyGameAssetProvider? existing = FindExisting(registry, options.ProviderId);
        KenneyGameAssetProvider provider;
        IReadOnlyList<string> warnings;

        if (existing is null)
        {
            provider = new KenneyGameAssetProvider(options);
            warnings = provider.Warnings;

            try
            {
                registry.Register(provider);
            }
            catch
            {
                //An identifier the registry will not take leaves nothing registered, so the archives
                //  this provider opened have to be closed again
                provider.Dispose();
                throw;
            }
        }
        else
        {
            provider = existing;
            warnings = provider.AddSources(options with { ProviderId = provider.ProviderId });
        }

        LogRegistration(provider, warnings);

        return provider;
    }

    //The provider already serving an identifier, or null. Another kind of provider under that
    //  identifier is a mistake worth stopping for: registering over it would dispose it, and whatever
    //  put it there is still holding its keys.
    private static KenneyGameAssetProvider? FindExisting(
        GameAssetProviderRegistry registry, string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) { return null; }

        foreach (IGameAssetProvider registered in registry.Providers)
        {
            if (!string.Equals(registered.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (registered is KenneyGameAssetProvider kenney) { return kenney; }

            throw new InvalidOperationException(
                $"The asset provider identifier '{providerId}' is already taken by a " +
                $"{registered.GetType().Name}. Give the Kenney assets another identifier with " +
                $"{nameof(KenneyAssetsOptions)}.{nameof(KenneyAssetsOptions.ProviderId)}, or unregister " +
                "that provider first.");
        }

        return null;
    }

    private static void LogRegistration(
        KenneyGameAssetProvider provider, IReadOnlyList<string> warnings)
    {
        ILogger<Engine> logger = Engine.Logger;

        logger.LogInformation(
            "Kenney assets registered as '{ProviderId}': {PackCount} pack(s), {AssetCount} asset(s).",
            provider.ProviderId,
            provider.Packs.Count,
            provider.AssetCount);

        //Only what THIS call could not do exactly; the rest is already in the log from earlier calls
        foreach (string warning in warnings)
        {
            logger.LogWarning("Kenney assets ('{ProviderId}'): {Warning}", provider.ProviderId, warning);
        }
    }
}
