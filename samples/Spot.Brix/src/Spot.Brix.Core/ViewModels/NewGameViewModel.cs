using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Spot.Brix.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace Spot.Brix.ViewModels;

/// <summary>
/// One entry in a colour picker: the game's <see cref="ColorItem"/> plus the brush that paints its
/// swatch in XAML.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class ColorChoice
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ColorChoice"/> class.
    /// </summary>
    /// <param name="item">The game colour this choice stands for.</param>
    public ColorChoice(ColorItem item)
    {
        Item = item;
        Brush = new SolidColorBrush(Color.FromArgb(item.Color.Alpha, item.Color.Red, item.Color.Green, item.Color.Blue));
    }

    /// <summary>Gets the game colour this choice stands for.</summary>
    public ColorItem Item { get; }

    /// <summary>Gets the colour's display name.</summary>
    public string Name => Item.Name;

    /// <summary>Gets the brush used to paint the swatch next to the name.</summary>
    public SolidColorBrush Brush { get; }
}

/// <summary>
/// One player row in the New Game dialog: the name, whether a person or the AI plays it, and which
/// colour the player's spots are.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class NewGamePlayerViewModel : SimpleViewModel
{
    private readonly Action<NewGamePlayerViewModel> _colorChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="NewGamePlayerViewModel"/> class.
    /// </summary>
    /// <param name="seat">The one-based seat number, used for the row's heading.</param>
    /// <param name="name">The player's starting name.</param>
    /// <param name="typeIndex">0 for a human player, 1 for a computer player.</param>
    /// <param name="colorIndex">The player's starting index into <see cref="ColorChoices"/>.</param>
    /// <param name="colorChanged">Called when this row's colour changes, so duplicates can be resolved.</param>
    public NewGamePlayerViewModel(int seat,
                                  string name,
                                  int typeIndex,
                                  int colorIndex,
                                  Action<NewGamePlayerViewModel> colorChanged)
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Seat = seat;
        _name = name;
        _typeIndex = typeIndex;
        _colorIndex = colorIndex;
        _colorChanged = colorChanged;
    }

    /// <summary>Gets the one-based seat number this row configures.</summary>
    public int Seat { get; }

    /// <summary>Gets the heading shown above the row.</summary>
    public string Heading => $"Player {Seat}";

    /// <summary>The colours a player can be given, in display order. Shared by every row.</summary>
    internal static readonly IReadOnlyList<ColorChoice> AllColorChoices =
        GameConfig.AvailableColors.Select(c => new ColorChoice(c)).ToList();

    private static readonly IReadOnlyList<string> AllPlayerTypeNames = ["Human", "Computer"];

    /// <summary>
    /// Gets the colours a player can be given, in display order. This is an instance property because
    /// XAML data binding resolves paths against the bound instance.
    /// </summary>
    public IReadOnlyList<ColorChoice> ColorChoices => AllColorChoices;

    /// <summary>Gets the player-type choices, in the order the picker shows them.</summary>
    public IReadOnlyList<string> PlayerTypeNames => AllPlayerTypeNames;

    private string _name = string.Empty;

    /// <summary>Gets or sets the player's name.</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private int _typeIndex;

    /// <summary>Gets or sets the index into <see cref="PlayerTypeNames"/>: 0 human, 1 computer.</summary>
    public int TypeIndex
    {
        get => _typeIndex;
        set => SetProperty(ref _typeIndex, value);
    }

    private int _colorIndex;

    /// <summary>Gets or sets the index into <see cref="ColorChoices"/> of the player's colour.</summary>
    public int ColorIndex
    {
        get => _colorIndex;
        set
        {
            if (_colorIndex == value || value < 0)
                return;

            SetProperty(ref _colorIndex, value);
            _colorChanged?.Invoke(this);
        }
    }

    private bool _isSeated = true;

    /// <summary>
    /// Gets or sets whether this seat is part of the game. Seats 3 and 4 are dropped when a shorter
    /// game is chosen.
    /// </summary>
    public bool IsSeated
    {
        get => _isSeated;
        set
        {
            SetProperty(ref _isSeated, value);
            NotifyPropertyChanged(nameof(RowVisibility));
        }
    }

    /// <summary>Gets the visibility of the row, following <see cref="IsSeated"/>.</summary>
    public Visibility RowVisibility => _isSeated ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Builds the game-side player this row describes.
    /// </summary>
    /// <returns>A player with the row's name, type and colour.</returns>
    public Player ToPlayer() => new()
    {
        Name = string.IsNullOrWhiteSpace(_name) ? Heading : _name.Trim(),
        Type = _typeIndex == 0 ? PlayerType.Human : PlayerType.Computer,
        ColorItem = AllColorChoices[Math.Clamp(_colorIndex, 0, AllColorChoices.Count - 1)].Item,
    };
}

/// <summary>
/// The New Game dialog's model: how many players, how big the board is, and the four player rows.
/// </summary>
/// <remarks>
/// The same instance is reused for every visit to the dialog, so the previous game's choices are what
/// the player sees the next time it opens — the behaviour of the reference implementation.
/// </remarks>
[Microsoft.UI.Xaml.Data.Bindable]
public class NewGameViewModel : SimpleViewModel
{
    private bool _resolvingColors;

    /// <summary>
    /// Initializes a new instance of the <see cref="NewGameViewModel"/> class with the sample's
    /// defaults: four players (one human, three computer) on an 8×8 board.
    /// </summary>
    public NewGameViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Player1 = CreatePlayer(1, 0);
        Player2 = CreatePlayer(2, 1);
        Player3 = CreatePlayer(3, 2);
        Player4 = CreatePlayer(4, 3);

        _playerCountIndex = GameConfig.DefaultPlayerCountIndex;
        _boardWidthIndex = GameConfig.DefaultBoardSizeIndex;
        _boardHeightIndex = GameConfig.DefaultBoardSizeIndex;

        ApplyPlayerCount();
    }

    private NewGamePlayerViewModel CreatePlayer(int seat, int colorIndex)
        => new(seat,
               GameConfig.DefaultPlayerNames[seat - 1],
               typeIndex: seat == 1 ? 0 : 1,
               colorIndex: colorIndex,
               colorChanged: ResolveDuplicateColors);

    #region | Choice lists |

    /// <summary>Gets the selectable player counts, in the order the picker shows them.</summary>
    public IReadOnlyList<string> PlayerCounts { get; } =
        Enumerable.Range(GameConfig.MinimumPlayerCount,
                         GameConfig.MaximumPlayerCount - GameConfig.MinimumPlayerCount + 1)
                  .Select(n => n.ToString())
                  .ToList();

    /// <summary>Gets the selectable board dimensions, in the order the pickers show them.</summary>
    public IReadOnlyList<string> BoardSizes { get; } = GameConfig.BoardSizes;

    #endregion

    #region | Bindable properties |

    private int _playerCountIndex;

    /// <summary>Gets or sets the index into <see cref="PlayerCounts"/> of the chosen player count.</summary>
    public int PlayerCountIndex
    {
        get => _playerCountIndex;
        set
        {
            if (value < 0)
                return;

            SetProperty(ref _playerCountIndex, value);
            ApplyPlayerCount();
            NotifyPropertyChanged(nameof(PlayerCount));
        }
    }

    /// <summary>Gets the number of players the game will be started with.</summary>
    public int PlayerCount => GameConfig.MinimumPlayerCount + _playerCountIndex;

    private int _boardWidthIndex;

    /// <summary>Gets or sets the index into <see cref="BoardSizes"/> of the chosen board width.</summary>
    public int BoardWidthIndex
    {
        get => _boardWidthIndex;
        set
        {
            if (value < 0)
                return;

            SetProperty(ref _boardWidthIndex, value);
        }
    }

    private int _boardHeightIndex;

    /// <summary>Gets or sets the index into <see cref="BoardSizes"/> of the chosen board height.</summary>
    public int BoardHeightIndex
    {
        get => _boardHeightIndex;
        set
        {
            if (value < 0)
                return;

            SetProperty(ref _boardHeightIndex, value);
        }
    }

    /// <summary>Gets the first player row.</summary>
    public NewGamePlayerViewModel Player1 { get; } = null!;

    /// <summary>Gets the second player row.</summary>
    public NewGamePlayerViewModel Player2 { get; } = null!;

    /// <summary>Gets the third player row; hidden for a two-player game.</summary>
    public NewGamePlayerViewModel Player3 { get; } = null!;

    /// <summary>Gets the fourth player row; hidden for a two- or three-player game.</summary>
    public NewGamePlayerViewModel Player4 { get; } = null!;

    #endregion

    /// <summary>
    /// Builds the options the game host needs from the current choices.
    /// </summary>
    /// <returns>The board size and the seated players, in seating order.</returns>
    public NewGameOptions CreateOptions()
    {
        var options = new NewGameOptions
        {
            BoardWidth = GameConfig.MinimumBoardSize + _boardWidthIndex,
            BoardHeight = GameConfig.MinimumBoardSize + _boardHeightIndex,
        };

        foreach (var player in AllPlayers().Where(p => p.IsSeated))
        {
            options.Players.Add(player.ToPlayer());
        }

        return options;
    }

    private IEnumerable<NewGamePlayerViewModel> AllPlayers()
    {
        yield return Player1;
        yield return Player2;
        yield return Player3;
        yield return Player4;
    }

    private void ApplyPlayerCount()
    {
        int seated = PlayerCount;

        foreach (var player in AllPlayers())
        {
            player.IsSeated = player.Seat <= seated;
        }

        ResolveDuplicateColors(null);
    }

    /// <summary>
    /// Keeps the seated players on distinct colours: any other seat wearing the colour just chosen is
    /// moved to the first colour nobody is using.
    /// </summary>
    private void ResolveDuplicateColors(NewGamePlayerViewModel changed)
    {
        if (_resolvingColors)
            return;

        _resolvingColors = true;

        try
        {
            var seated = AllPlayers().Where(p => p.IsSeated).ToList();

            foreach (var player in seated)
            {
                if (ReferenceEquals(player, changed))
                    continue;

                bool clashes = seated.Any(other => !ReferenceEquals(other, player)
                                                   && other.ColorIndex == player.ColorIndex
                                                   && (ReferenceEquals(other, changed) || other.Seat < player.Seat));

                if (!clashes)
                    continue;

                var used = seated.Where(p => !ReferenceEquals(p, player))
                                 .Select(p => p.ColorIndex)
                                 .ToHashSet();

                for (int i = 0; i < NewGamePlayerViewModel.AllColorChoices.Count; i++)
                {
                    if (used.Contains(i))
                        continue;

                    player.ColorIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _resolvingColors = false;
        }
    }
}
