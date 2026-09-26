using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.Host.Links;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// Headless tests for the logic behind <see cref="ExternalLinks"/>: launching on the UI thread, posting to it
/// from elsewhere, and answering <see langword="false"/> - never faulting - when a link cannot be opened.
/// </summary>
public class ExternalLinkOpenerTests
{
    private static readonly Uri Link = new("https://example.com/credits");

    private readonly List<Uri> _launched = new();
    private readonly Queue<Action> _posted = new();

    private ExternalLinkOpener NewOpener(
        bool onUiThread = false,
        bool postAccepted = true,
        Func<Uri, Task<bool>>? launch = null) =>
        new(
            () => onUiThread,
            action =>
            {
                if (postAccepted)
                    _posted.Enqueue(action);
                return postAccepted;
            },
            launch ?? (uri =>
            {
                _launched.Add(uri);
                return Task.FromResult(true);
            }));

    [Fact]
    public async Task on_the_UI_thread_the_link_is_launched_directly()
    {
        //Act
        bool opened = await NewOpener(onUiThread: true).OpenAsync(Link);

        //Assert
        opened.Should().BeTrue();
        _launched.Should().Equal(Link);
        _posted.Should().BeEmpty();
    }

    [Fact]
    public async Task off_the_UI_thread_the_launch_waits_for_the_UI_thread()
    {
        //Arrange
        var opener = NewOpener();

        //Act
        Task<bool> pending = opener.OpenAsync(Link);
        bool completedBeforeThePost = pending.IsCompleted;
        _posted.Dequeue()();
        bool opened = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        //Assert
        completedBeforeThePost.Should().BeFalse();
        opened.Should().BeTrue();
        _launched.Should().Equal(Link);
    }

    [Fact]
    public async Task a_refused_post_answers_false()
    {
        //Act
        bool opened = await NewOpener(postAccepted: false).OpenAsync(Link);

        //Assert
        opened.Should().BeFalse();
        _launched.Should().BeEmpty();
    }

    [Fact]
    public async Task a_post_that_throws_answers_false()
    {
        //Arrange
        var opener = new ExternalLinkOpener(() => false, _ => throw new InvalidOperationException("no dispatcher"),
            _ => Task.FromResult(true));

        //Act
        bool opened = await opener.OpenAsync(Link);

        //Assert
        opened.Should().BeFalse();
    }

    [Fact]
    public async Task a_launcher_refusal_answers_false() =>
        (await NewOpener(onUiThread: true, launch: _ => Task.FromResult(false)).OpenAsync(Link)).Should().BeFalse();

    [Fact]
    public async Task a_launcher_failure_answers_false_instead_of_faulting()
    {
        //Arrange
        var opener = NewOpener(launch: _ => Task.FromException<bool>(new InvalidOperationException("no browser")));

        //Act
        Task<bool> pending = opener.OpenAsync(Link);
        _posted.Dequeue()();
        bool opened = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        //Assert
        opened.Should().BeFalse();
    }

    [Fact]
    public async Task a_relative_uri_answers_false_without_launching()
    {
        //Act
        bool opened = await NewOpener(onUiThread: true).OpenAsync(new Uri("credits/page", UriKind.Relative));

        //Assert
        opened.Should().BeFalse();
        _launched.Should().BeEmpty();
    }

    [Fact]
    public void a_null_uri_is_a_programming_error()
    {
        //Act
        Action act = () => NewOpener().OpenAsync(null!);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExternalLinks_answers_false_for_text_that_is_not_an_absolute_uri() =>
        (await ExternalLinks.OpenAsync("not a link")).Should().BeFalse();
}
