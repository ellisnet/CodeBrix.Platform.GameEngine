using System;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

public class SfxVoicePoolTests
{
    [Fact]
    public void SelectVoiceSlot_returns_the_first_free_slot()
    {
        //Arrange
        var busy = new[] { true, false, true };
        var sequences = new long[] { 5, 0, 7 };
        var priorities = new[] { 0, 0, 0 };

        //Act
        var slot = SfxVoicePool.SelectVoiceSlot(SfxCullPolicy.RejectNew, busy, sequences, priorities, 0, out var culled);

        //Assert
        slot.Should().Be(1);
        culled.Should().BeFalse();
    }

    [Fact]
    public void SelectVoiceSlot_RejectNew_drops_the_trigger_when_full()
    {
        //Arrange
        var busy = new[] { true, true };
        var sequences = new long[] { 1, 2 };
        var priorities = new[] { 0, 0 };

        //Act
        var slot = SfxVoicePool.SelectVoiceSlot(SfxCullPolicy.RejectNew, busy, sequences, priorities, 99, out var culled);

        //Assert
        slot.Should().Be(-1);
        culled.Should().BeFalse();
    }

    [Fact]
    public void SelectVoiceSlot_CullOldest_steals_the_longest_playing_voice()
    {
        //Arrange
        var busy = new[] { true, true, true };
        var sequences = new long[] { 8, 3, 12 };
        var priorities = new[] { 0, 0, 0 };

        //Act
        var slot = SfxVoicePool.SelectVoiceSlot(SfxCullPolicy.CullOldest, busy, sequences, priorities, 0, out var culled);

        //Assert
        slot.Should().Be(1);
        culled.Should().BeTrue();
    }

    [Fact]
    public void SelectVoiceSlot_CullLowestPriority_steals_the_lowest_priority_oldest_on_tie()
    {
        //Arrange
        var busy = new[] { true, true, true, true };
        var sequences = new long[] { 4, 9, 2, 6 };
        var priorities = new[] { 5, 1, 1, 3 };

        //Act - two voices tie at priority 1; the older one (sequence 2, slot 2) is stolen.
        var slot = SfxVoicePool.SelectVoiceSlot(SfxCullPolicy.CullLowestPriority, busy, sequences, priorities, 2, out var culled);

        //Assert
        slot.Should().Be(2);
        culled.Should().BeTrue();
    }

    [Fact]
    public void SelectVoiceSlot_CullLowestPriority_drops_the_trigger_when_every_voice_outranks_it()
    {
        //Arrange
        var busy = new[] { true, true };
        var sequences = new long[] { 1, 2 };
        var priorities = new[] { 5, 7 };

        //Act
        var slot = SfxVoicePool.SelectVoiceSlot(SfxCullPolicy.CullLowestPriority, busy, sequences, priorities, 4, out var culled);

        //Assert
        slot.Should().Be(-1);
        culled.Should().BeFalse();
    }

    [Fact]
    public void SelectVoiceSlot_CullLowestPriority_steals_an_equal_priority_voice()
    {
        //Arrange
        var busy = new[] { true, true };
        var sequences = new long[] { 1, 2 };
        var priorities = new[] { 4, 7 };

        //Act - the new trigger matches the lowest playing priority, so it may steal.
        var slot = SfxVoicePool.SelectVoiceSlot(SfxCullPolicy.CullLowestPriority, busy, sequences, priorities, 4, out var culled);

        //Assert
        slot.Should().Be(0);
        culled.Should().BeTrue();
    }

    [Fact]
    public void Pool_construction_validates_size_and_starts_idle()
    {
        //Arrange
        Action zeroSize = () => _ = new SfxVoicePool(0);

        //Act + Assert
        zeroSize.Should().Throw<ArgumentOutOfRangeException>();

        using var pool = new SfxVoicePool(4);
        pool.Size.Should().Be(4);
        pool.ActiveVoiceCount.Should().Be(0);
        pool.CullPolicy.Should().Be(SfxCullPolicy.CullOldest);
    }

    [Fact]
    public void TryPlay_rejects_a_resource_that_was_not_preloaded()
    {
        //Arrange - raw-PCM resources are not preloaded, so the pool must refuse them
        //(decoding on trigger is the exact stutter the pool exists to avoid).
        var manager = AudioResourceManager.Instance;
        const string key = "sfx_pool_not_preloaded_test";
        using var pool = new SfxVoicePool(2);

        try
        {
            var resource = manager.LoadFromPcm(key, new byte[] { 0x80, 0x80 }, 8000, 8);

            //Act
            var played = pool.TryPlay(resource);

            //Assert
            played.Should().BeFalse();
            pool.ActiveVoiceCount.Should().Be(0);
        }
        finally
        {
            manager.Unload(key);
        }
    }

    [Fact]
    public void TryPlay_returns_false_for_an_unknown_key()
    {
        //Arrange
        using var pool = new SfxVoicePool(2);

        //Act + Assert
        pool.TryPlay("sfx_pool_no_such_key").Should().BeFalse();
    }
}
