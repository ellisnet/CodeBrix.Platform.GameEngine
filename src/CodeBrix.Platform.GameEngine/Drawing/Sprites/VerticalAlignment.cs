using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Sprites; //was previously: Gondwana.Drawing.Sprites;
/// <summary>
/// Specifies the vertical alignment of a sprite or element.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VerticalAlignment
{
    /// <summary>
    /// Align to the top.
    /// </summary>
    Top,

    /// <summary>
    /// Align to the middle (center).
    /// </summary>
    Middle,

    /// <summary>
    /// Align to the bottom.
    /// </summary>
    Bottom
}