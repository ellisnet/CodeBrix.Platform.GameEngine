using System;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// Covers how <see cref="GeneratedMusicProvider.Render"/> derives the stream's state, with a scripted
/// render source in place of a music session: what was actually RENDERED decides whether music is
/// heard, and the session's starved flag only tells a wait from a rest once the block is silent.
/// </summary>
public class GeneratedMusicProviderRenderStateTests
{
    private readonly float[] _left = new float[256];
    private readonly float[] _right = new float[256];

    [Fact]
    public void Render_reports_Playing_as_soon_as_an_audible_block_arrives_even_while_the_session_reads_starved()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        var source = new ScriptedRenderSource { Audible = true, IsStarved = true };
        provider.AttachRenderSourceForTests(source);

        //Act
        int written = provider.Render(_left, _right);

        //Assert
        written.Should().Be(_left.Length);
        provider.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void Render_stays_Starting_while_nothing_has_been_heard_yet()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        var source = new ScriptedRenderSource { Audible = false, IsStarved = false };
        provider.AttachRenderSourceForTests(source);

        //Act
        provider.Render(_left, _right);
        StreamingMusicState notStarved = provider.State;
        source.IsStarved = true;
        provider.Render(_left, _right);

        //Assert
        notStarved.Should().Be(StreamingMusicState.Starting, "silent opening bars are not music heard yet");
        provider.State.Should().Be(StreamingMusicState.Starting);
    }

    [Fact]
    public void Render_after_music_was_heard_reports_a_silent_starved_block_as_Starved_and_a_silent_rest_as_Playing()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        var source = new ScriptedRenderSource { Audible = true };
        provider.AttachRenderSourceForTests(source);
        provider.Render(_left, _right);

        //Act
        source.Audible = false;
        source.IsStarved = true;
        provider.Render(_left, _right);
        StreamingMusicState waiting = provider.State;
        source.IsStarved = false;
        provider.Render(_left, _right);

        //Assert
        waiting.Should().Be(StreamingMusicState.Starved);
        provider.State.Should().Be(StreamingMusicState.Playing, "a rest in the music is still the music playing");
    }

    [Fact]
    public void Render_reports_Faulted_when_the_session_ends_on_a_generator_error_and_then_renders_nothing()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        var error = new InvalidOperationException("the model stopped writing");
        var source = new ScriptedRenderSource { Audible = true, IsFinished = true, GenerationError = error };
        provider.AttachRenderSourceForTests(source);

        //Act
        provider.Render(_left, _right);
        int after = provider.Render(_left, _right);

        //Assert
        provider.State.Should().Be(StreamingMusicState.Faulted);
        provider.Fault.Should().BeSameAs(error);
        after.Should().Be(0);
    }

    [Fact]
    public void Render_keeps_Playing_when_a_follow_up_failed_but_the_music_plays_on()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        var source = new ScriptedRenderSource
        {
            Audible = true,
            GenerationError = new InvalidOperationException("the follow-up was refused"),
        };
        provider.AttachRenderSourceForTests(source);

        //Act
        provider.Render(_left, _right);

        //Assert
        provider.State.Should().Be(StreamingMusicState.Playing);
        provider.Fault.Should().BeNull();
    }

    [Fact]
    public void Render_reports_Stopped_when_the_music_reaches_its_end()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        var source = new ScriptedRenderSource { IsFinished = true };
        provider.AttachRenderSourceForTests(source);

        //Act
        provider.Render(_left, _right);

        //Assert
        provider.State.Should().Be(StreamingMusicState.Stopped);
        provider.Render(_left, _right).Should().Be(0);
    }
}
