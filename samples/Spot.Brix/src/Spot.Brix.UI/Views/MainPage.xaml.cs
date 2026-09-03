using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Spot.Brix.ViewModels;
using System;
using System.Threading.Tasks;

namespace Spot.Brix.Views;

public sealed partial class MainPage : Page, IShowNewGameDialog
{
    private IManageGameCanvas _gameCanvasManager;

    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);
            _gameCanvasManager = DataContext as IManageGameCanvas;
            (DataContext as MainViewModel)?.SetNewGameDialogHost(this);
        };

        this.InitializeComponent();

        GameCanvas.FirstStarted += (_, _) =>
        {
            _gameCanvasManager?.CanvasFirstStart(GameCanvas);
            FocusGameCanvas();
        };

        // Keyboard shortcuts (e.g. "S" to toggle scores) reach the game only while the game surface
        // holds keyboard focus. Clicking a toolbar button/checkbox moves focus to that control — and
        // because KeyDown bubbles to the focused control's ancestors (not to the sibling canvas), the
        // engine's keyboard poller then never sees the key. Hand focus straight back to the game
        // surface after any toolbar interaction. handledEventsToo: true is required because the
        // buttons/checkboxes mark PointerReleased as handled.
        Toolbar.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => FocusGameCanvas()),
            handledEventsToo: true);
    }

    // Defer to the dispatcher so focus is restored after the toolbar control finishes processing
    // the click that (re)took focus.
    private void FocusGameCanvas() =>
        DispatcherQueue.TryEnqueue(() => GameCanvas.Focus(FocusState.Programmatic));

    /// <summary>
    /// Shows the New Game dialog over this page and waits for the player to start or cancel.
    /// </summary>
    /// <param name="viewModel">The choices to show, and the ones the player edits.</param>
    /// <returns>
    /// The options to start a game with, or <see langword="null"/> when the player cancelled.
    /// </returns>
    public async Task<Spot.Brix.NewGameOptions> ShowNewGameDialogAsync(NewGameViewModel viewModel)
    {
        var dialog = new NewGameDialog(viewModel) { XamlRoot = XamlRoot };

        try
        {
            var result = await dialog.ShowAsync();

            return result == ContentDialogResult.Primary ? viewModel.CreateOptions() : null;
        }
        finally
        {
            //The game reads the keyboard from the surface, which lost focus to the dialog.
            FocusGameCanvas();
        }
    }
}
