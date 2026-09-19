using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// What a game tells <see cref="EngineKenneyAssetsExtensions.UseKenneyAssets(Engine, KenneyAssetsOptions)"/>
/// about the Kenney asset sources it is registering: where they are, what to call the provider that
/// serves them, and what to do with a source that cannot be read.
/// </summary>
/// <remarks>
/// <para>
/// Every member has a default that suits a game registering the bundles it shipped, so the usual call
/// is the shorter overload that takes only paths. Reach for this record to name a second provider, to
/// point at a folder that HOLDS pack folders, or to have a missing bundle fail loudly.
/// </para>
/// <para>
/// This is a record, so a game can keep one instance as its house settings and derive the rest with a
/// <c>with</c> expression.
/// </para>
/// </remarks>
public sealed record KenneyAssetsOptions
{
    /// <summary>
    /// Gets the asset sources to register: each one the path of a downloaded Kenney bundle (a .zip
    /// file) or of a folder extracted from one. Empty by default, which registers a provider with no
    /// assets in it yet.
    /// </summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>
    /// Gets the identifier that namespaces every asset key of the provider these sources are
    /// registered with, which is the part of a key before the colon. Defaults to
    /// <see cref="KenneyGameAssetProvider.DefaultProviderId"/>.
    /// </summary>
    /// <remarks>
    /// Registering twice with the same identifier ADDS the new sources to the provider already serving
    /// it. Two sets of assets that must stay apart - a game's own bundles and a mod's, say - take one
    /// identifier each. An identifier cannot contain a colon.
    /// </remarks>
    public string ProviderId { get; init; } = KenneyGameAssetProvider.DefaultProviderId;

    /// <summary>
    /// Gets a value indicating whether a source folder that carries no licence file of its own is
    /// searched for pack folders inside it, each of which becomes a pack. Defaults to
    /// <see langword="false"/>, which takes such a folder as one pack.
    /// </summary>
    /// <remarks>
    /// This is how a folder holding MANY packs is registered - the shape of Kenney's own "all in one"
    /// download, where each pack sits at <c>2D assets/&lt;Pack&gt;/</c> with its own
    /// <c>License.txt</c>. It has no effect on a zip file, which is always one pack, nor on a folder
    /// that carries a licence file of its own, which is always one pack too.
    /// </remarks>
    public bool RecursiveFolders { get; init; }

    /// <summary>
    /// Gets a value indicating whether a source that cannot be opened is recorded as a warning rather
    /// than thrown. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The default is what a shipped game wants: one missing or damaged bundle among several should
    /// cost the game those assets, not its start-up. The reason lands in
    /// <see cref="KenneyGameAssetProvider.Warnings"/> and in the engine log. Turn it off while
    /// developing, when a bundle that is not there is a mistake worth stopping for.
    /// </remarks>
    public bool IgnoreUnreadableSources { get; init; } = true;
}
