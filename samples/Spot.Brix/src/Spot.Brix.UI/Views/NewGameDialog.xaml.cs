using Microsoft.UI.Xaml.Controls;
using Spot.Brix.ViewModels;

namespace Spot.Brix.Views;

/// <summary>
/// The New Game dialog: player count, board size, and one row per player (name, human or computer,
/// colour). It is a thin shell over <see cref="NewGameViewModel"/>, which holds every choice.
/// </summary>
public sealed partial class NewGameDialog : ContentDialog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NewGameDialog"/> class.
    /// </summary>
    /// <param name="viewModel">The choices to show, and the ones the player edits.</param>
    public NewGameDialog(NewGameViewModel viewModel)
    {
        DataContext = viewModel;

        this.InitializeComponent();
    }
}
