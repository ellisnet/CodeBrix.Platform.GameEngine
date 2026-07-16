using CodeBrix.Platform.Simple;
using System.Diagnostics;

namespace CoordinateTest.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel
{
    public MainViewModel()
    {
        if (!IsDesignMode(true)) 
        {
            Debug.WriteLine("Main view model startup.");
        }
    }

    public string Greeting => "Hello from CoordinateTest!";
}
