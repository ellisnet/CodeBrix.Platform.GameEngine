using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct.Particles; //was previously: Gondwana.Drawing.Direct.Particles;
/// <summary>
/// Defines the spatial distribution pattern used when spawning particles within an emitter.
/// </summary>
public enum ParticleSpawnDistribution
{
    /// <summary>
    /// Particles are spawned uniformly within a rectangular area. This is the default distribution.
    /// </summary>
    Rectangle = 0, // default behavior
    
    /// <summary>
    /// Particles are spawned uniformly within an ellipse or circular disk.
    /// </summary>
    Ellipse = 1,   // uniform disk/ellipse
    
    /// <summary>
    /// Particles are spawned uniformly within a ring or annulus shape (hollow ellipse).
    /// </summary>
    Ring = 2,      // uniform annulus
    
    /// <summary>
    /// Particles are spawned with a Gaussian (normal) distribution centered at the emitter origin,
    /// with spawning clamped to ellipse bounds for a center-weighted effect.
    /// </summary>
    Gaussian = 3   // center-weighted, clamped to ellipse bounds
}
