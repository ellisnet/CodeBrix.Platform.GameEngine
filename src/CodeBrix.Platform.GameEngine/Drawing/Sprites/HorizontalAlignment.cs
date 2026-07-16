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
/// Specifies the horizontal alignment of an element.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HorizontalAlignment
{
    /// <summary>
    /// Align to the left.
    /// </summary>
    Left,
    /// <summary>
    /// Align to the center.
    /// </summary>
    Center,
    /// <summary>
    /// Align to the right.
    /// </summary>
    Right
}