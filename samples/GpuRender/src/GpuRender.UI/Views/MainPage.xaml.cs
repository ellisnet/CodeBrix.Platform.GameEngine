using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Controls;
using GpuRender.ViewModels;

namespace GpuRender.Views;

public sealed partial class MainPage : Page
{
    private IManageGameCanvas _gameCanvasManager;

    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);
            _gameCanvasManager = DataContext as IManageGameCanvas;
        };

        this.InitializeComponent();

        GameCanvas.FirstStarted += (_, _) => _gameCanvasManager?.CanvasFirstStart(GameCanvas);
    }
}
