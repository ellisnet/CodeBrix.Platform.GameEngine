using KenneyAssetsDemo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KenneyAssetsDemo.Views;

public sealed partial class MainPage : Page
{
    private IManageGameCanvas _gameCanvasManager;

    public MainPage()
    {
        DataContextChanged += (_, _) => _gameCanvasManager = DataContext as IManageGameCanvas;

        this.InitializeComponent();

        GameCanvas.FirstStarted += (_, _) =>
        {
            _gameCanvasManager?.CanvasFirstStart(GameCanvas);
            FocusGameCanvas();
        };
    }

    // The engine's keyboard poller only sees keys while the game surface holds keyboard focus,
    // so hand focus to the canvas as soon as the engine starts. There is no other control on
    // this page, so nothing takes it away again.
    private void FocusGameCanvas() =>
        DispatcherQueue.TryEnqueue(() => GameCanvas.Focus(FocusState.Programmatic));
}
