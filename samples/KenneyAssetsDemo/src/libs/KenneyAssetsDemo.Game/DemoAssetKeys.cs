namespace KenneyAssetsDemo.Game;

/// <summary>
/// The asset keys this demo asks the Kenney asset provider for, and the bundle files they come from.
/// </summary>
/// <remarks>
/// <para>
/// A Kenney asset key is <c>&lt;providerId&gt;:&lt;pack-slug&gt;/&lt;path-inside-the-pack&gt;</c>. The
/// provider identifier is <c>kenney</c> unless a game asks for another one. The pack slug is made from
/// the title line of the <c>License.txt</c> inside the bundle, so
/// <c>simulated_bundle.zip</c> (titled "Simulated Bundle (1.0)") is <c>simulated-bundle</c> and
/// <c>kenney_puzzle-pack-1.zip</c> (titled "Puzzle Pack (1.1)") is <c>puzzle-pack</c> - the slug comes
/// from what is INSIDE the bundle, not from the file name. The rest of the key is the file's path
/// inside the pack, with its extension dropped because every asset named here is one the provider can
/// materialize. Keys are matched case-insensitively.
/// </para>
/// <para>
/// Hard-coding the keys is the point of the exercise: a game that ships its bundles knows exactly what
/// is in them, and a key spelled out in source is a key a reader can follow into the bundle. A game
/// that discovers assets instead - a mod loader, an asset browser - asks
/// <c>provider.Describe(new GameAssetQuery { Kind = ... })</c> and reads the keys off the descriptors
/// it gets back.
/// </para>
/// </remarks>
internal static class DemoAssetKeys
{
    /// <summary>The mixed bundle: the tile map, the animated characters, the fonts and the sounds.</summary>
    internal const string SimulatedBundleFileName = "simulated_bundle.zip";

    /// <summary>The 2D art pack: the sprite atlases and the vector document.</summary>
    internal const string PuzzlePackFileName = "kenney_puzzle-pack-1.zip";

    /// <summary>
    /// The Tiled map imported into the scene: 26 x 15 cells of 18 pixels, four tile layers, two tile
    /// sets whose tiles are different sizes.
    /// </summary>
    internal const string Map = "kenney:simulated-bundle/Tiled/tilemap-example-a";

    /// <summary>
    /// The animated glTF character pre-rendered into sprite frames. Its texture is a separate file
    /// inside the same pack, which the provider resolves without the game having to name it.
    /// </summary>
    internal const string Character = "kenney:simulated-bundle/Models/GLB format/character-a";

    /// <summary>The sprite atlas the collectibles are cut out of: one sheet image plus an XML document naming 86 frames.</summary>
    internal const string CollectibleAtlas = "kenney:puzzle-pack/Spritesheet/spritesheet_default";

    /// <summary>The sound played when a collectible is picked up.</summary>
    internal const string PickUpSound = "kenney:simulated-bundle/Audio/radar2";

    /// <summary>The font the heads-up display is drawn in.</summary>
    internal const string HudFont = "kenney:simulated-bundle/Fonts/Kenney Future Narrow";

    /// <summary>The font the title line is drawn in.</summary>
    internal const string TitleFont = "kenney:simulated-bundle/Fonts/Kenney Space";

    /// <summary>
    /// An input-prompt ICON font: its glyphs live in the private use area and it carries no letters at
    /// all, so it draws prompts and never text. See <see cref="TouchPromptGlyph"/>.
    /// </summary>
    internal const string IconFont = "kenney:simulated-bundle/Fonts/kenney_input_touch";

    /// <summary>The SVG document rasterized into the badge in the corner of the display.</summary>
    internal const string VectorBadge = "kenney:puzzle-pack/Vector/puzzleAssets_vector";

    /// <summary>
    /// The code point of the first input-prompt glyph in <see cref="IconFont"/>. The font maps
    /// U+E000 to U+E01B - the Unicode private use area - and nothing else, so these code points are
    /// the only ones it can draw. U+E000 is a "tap" hand.
    /// </summary>
    internal const int FirstPromptCodePoint = 0xE000;

    /// <summary>
    /// The first glyph of <see cref="IconFont"/>, as a string a text element can be given.
    /// </summary>
    /// <remarks>
    /// Built from the code point rather than written as a string escape, so it cannot be mistaken
    /// for - or mangled into - the six literal characters of the escape itself.
    /// </remarks>
    internal static readonly string TouchPromptGlyph = char.ConvertFromUtf32(FirstPromptCodePoint);

    /// <summary>The names of the collectible frames inside <see cref="CollectibleAtlas"/>.</summary>
    /// <remarks>
    /// These are the atlas document's own frame names with the <c>.png</c> the document spells them
    /// with stripped off, which is what the materializer names the regions by default.
    /// </remarks>
    internal static readonly string[] CollectibleFrames =
    [
        "element_blue_diamond_glossy",
        "element_green_diamond_glossy",
        "element_red_diamond_glossy",
        "element_yellow_diamond_glossy",
        "element_purple_diamond_glossy",
    ];

    /// <summary>The animations of <see cref="Character"/> the sprite sheet is rendered for.</summary>
    /// <remarks>
    /// The model offers 27 of them; rendering all 27 from eight directions would cost a large sheet
    /// and most of the start-up time, so the demo asks for the two it plays.
    /// </remarks>
    internal static readonly string[] CharacterAnimations = ["idle", "walk"];
}
