using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Controls;

namespace CoordinateTest.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        //Doing this before InitializeComponent() - in case InitializeComponent()
        //  is the thing that sets the data context.
        DataContextChanged += (sender, args) =>
        {
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);
        };
    
        this.InitializeComponent();
    }
}
