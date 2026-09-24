using System;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// A render source whose next block and session flags the test sets: each render writes either
/// silence or a quiet tone, and the flags read whatever the test last set - so the provider's state
/// derivation can be driven through combinations a real session only produces now and then.
/// </summary>
internal sealed class ScriptedRenderSource : IMusicRenderSource
{
    /// <summary>Whether the next block is audible.</summary>
    internal bool Audible { get; set; }

    public bool IsPlaying { get; set; } = true;

    public bool IsStarved { get; set; }

    public bool IsFinished { get; set; }

    public Exception? GenerationError { get; set; }

    public void Render(Span<float> left, Span<float> right)
    {
        left.Clear();
        right.Clear();

        if (!Audible) { return; }

        for (var i = 0; i < left.Length; i++)
        {
            left[i] = i % 2 == 0 ? 0.1F : -0.1F;
            right[i] = -left[i];
        }
    }
}
