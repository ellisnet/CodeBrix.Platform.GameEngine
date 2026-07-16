using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Animation; //was previously: Gondwana.Drawing.Animation;
/// <summary>
/// Simple is self-terminating; the other two are repeating
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CycleType
{
    /// <summary>
    /// 1 -> 2 -> 3 -> 4 -> stop
    /// </summary>
    Simple,

    /// <summary>
    /// 1 -> 2 -> 3 -> 4 -> 1 -> 2 -> ...
    /// </summary>
    Repeating,

    /// <summary>
    /// 1 -> 2 -> 3 -> 4 -> 3 -> 2 -> 1 -> 2 -> ...
    /// </summary>
    PingPong
}