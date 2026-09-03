namespace CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS; //was previously: Gondwana.Drawing.Tilesheets.GTS;
/// <summary>
/// Indicates where a tilesheet definition came from.
/// </summary>
public enum TilesheetDefinitionSourceKind
{
    /// <summary>No provenance is known for the definition.</summary>
    None = 0,

    /// <summary>The definition was loaded from a loose .gts file on disk.</summary>
    LooseDefinitionFile = 1,

    /// <summary>The definition was loaded from a .gts entry packed inside an assets file.</summary>
    PackedDefinitionFile = 2,

    /// <summary>The definition was generated at run time from a tilesheet rather than read from a file.</summary>
    Generated = 3
}
