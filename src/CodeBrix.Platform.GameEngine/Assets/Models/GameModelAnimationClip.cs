using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Assets.Models; //CodeBrix (not from Gondwana)
/// <summary>
/// One animation of a <see cref="GameModel"/>, baked to vertex data: every frame carries the
/// evaluated positions and normals of every mesh of the model.
/// </summary>
/// <remarks>
/// <para>
/// Baking trades memory for a consumer that never has to know about skinning or node animation:
/// playing the clip is overwriting vertex data. The cost grows with vertices multiplied by
/// frames, which is why baking is opt-in.
/// </para>
/// <para>
/// THE THREE CONTRACT GUARANTEES a clip honours:
/// </para>
/// <para>
/// (1) ALIGNMENT — <c>Frames[f].Meshes[i]</c> corresponds to <c>Model.Meshes[i]</c> and carries
/// the same vertex count, for every frame and every mesh. A consumer therefore allocates its
/// vertex buffers once from the model and then only overwrites positions and normals per frame;
/// nothing is added, removed or reordered by a clip. Use
/// <see cref="IsCompatibleWith"/> to assert this against a particular model.
/// </para>
/// <para>
/// (2) UPLOAD-FRIENDLY PAYLOADS — each frame mesh holds flat <see cref="float"/> arrays in the
/// same layout as <see cref="GameModelMesh.Positions"/> and
/// <see cref="GameModelMesh.Normals"/>, ready to be handed to a graphics device as they are.
/// </para>
/// <para>
/// (3) TIMING BELONGS TO THE CONSUMER — the clip carries <see cref="Duration"/> and
/// <see cref="FrameRate"/> and holds no playback state. Its frames sample the half-open interval
/// <c>[0, Duration)</c> at a uniform spacing of <c>Duration / FrameCount</c>, so a looping clip
/// never repeats its end pose. A consumer advances its own clock and asks
/// <see cref="GetFrameIndex"/> which frame to show.
/// </para>
/// </remarks>
public sealed class GameModelAnimationClip
{
    /// <summary>
    /// Gets the name of the animation, as the source asset named it (for example <c>walk</c>).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the length of the animation in seconds. Always greater than zero for a clip a
    /// provider produced.
    /// </summary>
    public required float Duration { get; init; }

    /// <summary>
    /// Gets the rate the frames were baked at, in frames per second, as asked of the provider.
    /// The actual frame spacing is <see cref="Duration"/> divided by <see cref="FrameCount"/>,
    /// which can differ slightly because a whole number of frames has to cover the duration.
    /// </summary>
    public required int FrameRate { get; init; }

    /// <summary>
    /// Gets the baked frames, first to last, sampling <c>[0, Duration)</c>.
    /// </summary>
    public required IReadOnlyList<GameModelAnimationFrame> Frames { get; init; }

    /// <summary>
    /// Gets the number of baked frames.
    /// </summary>
    public int FrameCount => Frames.Count;

    /// <summary>
    /// Maps a playback time to the frame of this clip that should be shown.
    /// </summary>
    /// <param name="timeSeconds">The elapsed playback time in seconds. May exceed
    /// <see cref="Duration"/>, and may be negative.</param>
    /// <param name="loop">
    /// <see langword="true"/> to wrap a time outside <c>[0, Duration)</c> back into the clip, so
    /// playback repeats; <see langword="false"/> to clamp it to the first or last frame.
    /// </param>
    /// <returns>
    /// A valid index into <see cref="Frames"/>, or <c>0</c> when the clip has no frames or no
    /// duration (a caller that may hold such a clip checks <see cref="FrameCount"/> first).
    /// </returns>
    public int GetFrameIndex(double timeSeconds, bool loop = true)
    {
        int frameCount = Frames.Count;

        if (frameCount <= 0 || Duration <= 0.0f)
            return 0;

        double position = timeSeconds / Duration * frameCount;

        if (loop)
        {
            double wrapped = position % frameCount;

            if (wrapped < 0.0)
                wrapped += frameCount;

            int looped = (int)wrapped;

            return looped >= frameCount ? frameCount - 1 : looped;
        }

        if (position <= 0.0)
            return 0;

        int clamped = (int)position;

        return clamped >= frameCount ? frameCount - 1 : clamped;
    }

    /// <summary>
    /// Checks guarantee (1), the alignment of this clip with a model: every frame carries one
    /// frame mesh per model mesh, in the same order, with the same vertex count.
    /// </summary>
    /// <param name="model">The model the clip is to be played on.</param>
    /// <returns>
    /// <see langword="true"/> when the clip can be played on the model; otherwise
    /// <see langword="false"/>, which is also the answer for a clip with no frames, since such a
    /// clip cannot be played at all.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is null.</exception>
    public bool IsCompatibleWith(GameModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (Frames.Count == 0)
            return false;

        int meshCount = model.Meshes.Count;

        foreach (var frame in Frames)
        {
            if (frame.Meshes.Count != meshCount)
                return false;

            for (int i = 0; i < meshCount; i++)
            {
                if (frame.Meshes[i].Positions.Length != model.Meshes[i].Positions.Length)
                    return false;
            }
        }

        return true;
    }
}
