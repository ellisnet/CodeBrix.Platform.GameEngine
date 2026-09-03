using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Platformer.Brix.ViewModels;

namespace Platformer.Brix.Views;

public sealed partial class MainPage : Page
{
    private IManageGameCanvas _gameCanvasManager;

    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            if (_gameCanvasManager is not null)
                _gameCanvasManager.ExitRequested -= OnExitRequested;

            _gameCanvasManager = DataContext as IManageGameCanvas;

            if (_gameCanvasManager is not null)
                _gameCanvasManager.ExitRequested += OnExitRequested;
        };

        this.InitializeComponent();

        GameCanvas.FirstStarted += (_, _) =>
        {
            _gameCanvasManager?.CanvasFirstStart(GameCanvas);
            FocusGameCanvas();
        };
    }

    // The engine's keyboard poller only sees keys while the game surface holds keyboard focus,
    // so hand focus to the canvas as soon as the engine starts.
    private void FocusGameCanvas() =>
        DispatcherQueue.TryEnqueue(() => GameCanvas.Focus(FocusState.Programmatic));

    // The game raises this when the player presses Esc.
    private void OnExitRequested(object sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(App.RequestExit);
}
