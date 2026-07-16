using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine; //was previously: Gondwana;
/// <summary>
/// Provides centralized access to all engine resource managers.
/// </summary>
public sealed class EngineManagers
{
    internal EngineManagers() { }

    /// <summary>
    /// Gets the audio resource manager for loading and managing audio assets.
    /// </summary>
    public AudioResourceManager AudioResources { get; } = AudioResourceManager.Instance;

    /// <summary>
    /// Gets the direct drawing manager for immediate-mode rendering operations.
    /// </summary>
    public DirectDrawingManager DirectDrawings { get; } = DirectDrawingManager.Instance;

    /// <summary>
    /// Gets the font manager for loading and managing font resources.
    /// </summary>
    public FontManager Fonts { get; } = FontManager.Instance;

    /// <summary>
    /// Gets the sprite manager for managing sprite assets and rendering.
    /// </summary>
    public SpriteManager Sprites { get; } = SpriteManager.Instance;

    /// <summary>
    /// Gets the tilesheet registry for managing tilesheet resources.
    /// </summary>
    public TilesheetRegistry Tilesheets { get; } = TilesheetRegistry.Instance;

    /// <summary>
    /// Gets the SVG resource manager for loading and managing SVG assets.
    /// </summary>
    public SvgResourceManager SvgResources { get; } = SvgResourceManager.Instance;
}
