using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Coordinates; //was previously: Gondwana.Drawing.Coordinates;
/// <summary>
/// Identifies the layout and math rules used to map tiles to world pixels
/// and world pixels back to grid coordinates. Each option defines a different
/// tile geometry.
/// Used by SceneLayer to choose the correct coordinate math for a given map.
/// </summary>
public enum CoordinateSystemTypes
{
    /// <summary>
    /// Axis-aligned square grid (Cartesian lattice).
    /// </summary>
    Orthogonal = 0,

    /// <summary>
    /// Isometric projection using a rhombic (diamond) lattice.
    /// </summary>
    IsometricRhombic = 1,

    /// <summary>
    /// Isometric projection using an underlying square lattice
    /// with diagonal basis vectors.
    /// </summary>
    IsometricAxial = 2,

    /// <summary>
    /// Hexagonal grid using axial coordinates with flat-topped hexes.
    /// </summary>
    HexAxialFlatTop = 3,

    /// <summary>
    /// Hexagonal grid using axial coordinates with pointy-topped hexes.
    /// </summary>
    HexAxialPointedTop = 4,

    /// <summary>
    /// Oblique projection using a right-receding, sheared square lattice: columns stay
    /// horizontal while rows advance down and to the right, giving each tile a
    /// parallelogram footprint rather than an isometric diamond.
    /// </summary>
    Oblique = 5
}
