using System;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct; //CodeBrix (not from Gondwana)

/// <summary>
/// A world-space health bar that floats above a <see cref="Sprite"/> and follows it automatically.
/// </summary>
/// <remarks>
/// <para>
/// The bar is a <see cref="DirectComposite"/> in <see cref="DirectDrawingMode.SceneLayer"/> holding two
/// <see cref="DirectRectangle"/> children: an outlined track and an inset fill whose width is the current
/// <see cref="Value"/> as a fraction of <see cref="MaxValue"/>.
/// </para>
/// <para>
/// The bar subscribes to <see cref="Sprite.SpriteMoved"/>, so gameplay code never has to reposition it,
/// and to <see cref="Sprite.Disposing"/>, so it disposes itself when the sprite it belongs to goes away.
/// Gameplay code only sets <see cref="Value"/> (or calls <see cref="SetValue"/>).
/// </para>
/// <para>
/// Threshold colours are opt-in: by default the fill always uses <see cref="FillColor"/>. Calling
/// <see cref="SetThresholdColors"/> (or setting <see cref="UseThresholdColors"/>) switches the fill to
/// <see cref="WarningColor"/> at or below <see cref="WarningFraction"/> and to <see cref="CriticalColor"/>
/// at or below <see cref="CriticalFraction"/>.
/// </para>
/// <para>
/// The bar is drawn in world pixels on the target's own scene layer, so it scales and scrolls with the
/// camera exactly like the sprite does.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var bar = new HealthBar(renderSurfaceHost, playerSprite, maxValue: 100f, size: new Size(72, 9),
///                         nickname: "player-health");
///
/// bar.SetThresholdColors(Color.FromArgb(245, 240, 190, 60), Color.FromArgb(245, 235, 70, 60));
/// bar.SetZOrder(200);
/// bar.Show();
///
/// // Later, when the player is hit:
/// bar.Value = playerHealth;
/// </code>
/// </example>
public sealed class HealthBar : DirectComposite
{
    /// <summary>The inset, in world pixels, of the fill inside the track on every edge.</summary>
    public const int InnerPadding = 2;

    /// <summary>The default gap, in world pixels, between the bottom of the bar and the top of the target.</summary>
    public const int DefaultGapPx = 6;

    private static readonly Size DefaultSize = new(64, 9);

    private readonly DirectRectangle _track;
    private readonly DirectRectangle _fill;

    private Color _fillColor = Color.FromArgb(245, 55, 210, 105);
    private Color _warningColor = Color.FromArgb(245, 240, 190, 60);
    private Color _criticalColor = Color.FromArgb(245, 235, 70, 60);
    private Color _appliedFillColor;

    private float _maxValue;
    private float _value;
    private float _warningFraction = 0.5f;
    private float _criticalFraction = 0.25f;
    private bool _useThresholdColors;
    private Size _barSize;
    private Point _offsetPx;
    private bool _disposed;

    #region Construction

    /// <summary>
    /// Creates a health bar that follows <paramref name="target"/> on the target's own scene layer.
    /// </summary>
    /// <param name="renderSurfaceHost">The render surface host that owns the target's scene.</param>
    /// <param name="target">The sprite the bar follows.</param>
    /// <param name="maxValue">The value that fills the bar completely. Must be greater than zero.</param>
    /// <param name="size">The outer bar size in world pixels. Defaults to 64 x 9.</param>
    /// <param name="offsetPx">
    /// An extra world-pixel offset applied to the position centred above the target. Defaults to none.
    /// </param>
    /// <param name="nickname">
    /// An optional diagnostic nickname. When omitted, a unique one is derived from the target's nickname so
    /// two bars never collide in the direct-drawing registry.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="renderSurfaceHost"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxValue"/> is not greater than zero, or the size is too small to draw.</exception>
    public HealthBar(RenderSurfaceHostBase renderSurfaceHost,
                     Sprite target,
                     float maxValue,
                     Size? size = null,
                     Point? offsetPx = null,
                     string? nickname = null)
        : base(renderSurfaceHost,
               DirectDrawingMode.SceneLayer,
               PointF.Empty,
               ValidateAndResolveNickname(renderSurfaceHost, target, maxValue, size ?? DefaultSize, nickname))
    {
        Target = target;

        _maxValue = maxValue;
        _value = maxValue;
        _barSize = size ?? DefaultSize;
        _offsetPx = offsetPx ?? Point.Empty;
        _appliedFillColor = _fillColor;

        _track = new DirectRectangle(
                Color.FromArgb(220, 20, 24, 31),
                renderSurfaceHost,
                target.SceneLayer,
                new Rectangle(Point.Empty, _barSize),
                $"{Nickname}-track")
            .SetFilled(true)
            .SetBorderColor(Color.FromArgb(235, 235, 241, 247))
            .SetStrokeWidth(1f)
            .SetStrokeAlign(DirectRectangle.StrokeAlign.Inside)
            .SetCornerRadius(2f);

        _fill = new DirectRectangle(
                _fillColor,
                renderSurfaceHost,
                target.SceneLayer,
                GetFillBounds(Point.Empty),
                $"{Nickname}-fill")
            .SetFilled(true)
            .SetStrokeWidth(0f)
            .SetCornerRadius(1f);

        Add(_track, keepCurrentOffset: false, explicitLocalOffsetPx: Vector2.Zero);
        Add(_fill, keepCurrentOffset: false, explicitLocalOffsetPx: new Vector2(InnerPadding, InnerPadding));

        Target.SpriteMoved += OnTargetMoved;
        Target.Disposing += OnTargetDisposing;

        RefreshPosition();
        UpdateFill();
    }

    /// <summary>
    /// Creates a health bar on an explicit scene layer, sized and offset with plain pixel values.
    /// </summary>
    /// <param name="renderSurfaceHost">The render surface host that owns the scene.</param>
    /// <param name="sceneLayer">
    /// The scene layer the bar is drawn on. It must be the layer <paramref name="target"/> belongs to.
    /// </param>
    /// <param name="target">The sprite the bar follows.</param>
    /// <param name="maxValue">The value that fills the bar completely. Must be greater than zero.</param>
    /// <param name="width">The outer bar width in world pixels.</param>
    /// <param name="height">The outer bar height in world pixels.</param>
    /// <param name="offsetY">
    /// An extra vertical world-pixel offset applied above the target; negative values raise the bar.
    /// </param>
    /// <param name="nickname">An optional diagnostic nickname.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sceneLayer"/> is not the layer of <paramref name="target"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxValue"/> is not greater than zero, or the size is too small to draw.</exception>
    public HealthBar(RenderSurfaceHostBase renderSurfaceHost,
                     SceneLayer sceneLayer,
                     Sprite target,
                     float maxValue,
                     int width,
                     int height,
                     int offsetY = 0,
                     string? nickname = null)
        : this(renderSurfaceHost,
               RequireTargetOnLayer(sceneLayer, target),
               maxValue,
               new Size(width, height),
               new Point(0, offsetY),
               nickname)
    {
    }

    private static Sprite RequireTargetOnLayer(SceneLayer sceneLayer, Sprite target)
    {
        ArgumentNullException.ThrowIfNull(sceneLayer);
        ArgumentNullException.ThrowIfNull(target);

        if (!ReferenceEquals(sceneLayer, target.SceneLayer))
        {
            throw new ArgumentException(
                "The health bar layer must be the scene layer the target sprite belongs to.",
                nameof(sceneLayer));
        }

        return target;
    }

    private static string ValidateAndResolveNickname(RenderSurfaceHostBase renderSurfaceHost,
                                                     Sprite target,
                                                     float maxValue,
                                                     Size size,
                                                     string? nickname)
    {
        ArgumentNullException.ThrowIfNull(renderSurfaceHost);
        ArgumentNullException.ThrowIfNull(target);

        if (maxValue <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxValue),
                maxValue,
                "The maximum health-bar value must be greater than zero.");
        }

        ValidateSize(size, nameof(size));

        return string.IsNullOrWhiteSpace(nickname)
            ? $"{target.Nickname ?? "sprite"}-health-{Guid.NewGuid():N}"
            : nickname!;
    }

    private static void ValidateSize(Size size, string parameterName)
    {
        int minimum = (InnerPadding * 2) + 1;

        if (size.Width < minimum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                size,
                $"The health-bar width must be at least {minimum} world pixels.");
        }

        if (size.Height < minimum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                size,
                $"The health-bar height must be at least {minimum} world pixels.");
        }
    }

    #endregion Construction

    #region Properties

    /// <summary>
    /// Gets the sprite this bar follows.
    /// </summary>
    public Sprite Target { get; }

    /// <summary>
    /// Gets or sets the value that fills the bar completely. Setting it re-clamps <see cref="Value"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public float MaxValue
    {
        get => _maxValue;
        set
        {
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The maximum health-bar value must be greater than zero.");
            }

            _maxValue = value;
            _value = Math.Clamp(_value, 0f, _maxValue);

            UpdateFill();
        }
    }

    /// <summary>
    /// Gets or sets the current value. Values are clamped to zero through <see cref="MaxValue"/>.
    /// </summary>
    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, 0f, _maxValue);

            if (_value.Equals(clamped))
                return;

            _value = clamped;

            UpdateFill();
        }
    }

    /// <summary>
    /// Gets the filled portion of the bar, from zero through one.
    /// </summary>
    public float Fraction => _value / _maxValue;

    /// <summary>
    /// Gets or sets the outer bar size in world pixels.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The size is too small to draw.</exception>
    public Size BarSize
    {
        get => _barSize;
        set
        {
            ValidateSize(value, nameof(value));

            if (_barSize == value)
                return;

            _barSize = value;
            _track.WorldBounds = new Rectangle(_track.WorldBounds.Location, _barSize);

            RefreshPosition();
            UpdateFill();
        }
    }

    /// <summary>
    /// Gets or sets the extra world-pixel offset applied to the position centred above the target.
    /// </summary>
    public Point OffsetPx
    {
        get => _offsetPx;
        set
        {
            if (_offsetPx == value)
                return;

            _offsetPx = value;

            RefreshPosition();
        }
    }

    /// <summary>
    /// Gets or sets the fill colour used while the bar is above <see cref="WarningFraction"/>, and at all
    /// times while <see cref="UseThresholdColors"/> is <see langword="false"/>.
    /// </summary>
    public Color FillColor
    {
        get => _fillColor;
        set
        {
            _fillColor = value;

            UpdateFill();
        }
    }

    /// <summary>
    /// Gets or sets the fill colour used at or below <see cref="WarningFraction"/>.
    /// </summary>
    public Color WarningColor
    {
        get => _warningColor;
        set
        {
            _warningColor = value;

            UpdateFill();
        }
    }

    /// <summary>
    /// Gets or sets the fill colour used at or below <see cref="CriticalFraction"/>.
    /// </summary>
    public Color CriticalColor
    {
        get => _criticalColor;
        set
        {
            _criticalColor = value;

            UpdateFill();
        }
    }

    /// <summary>
    /// Gets or sets the fraction at or below which <see cref="WarningColor"/> is used. Clamped to zero
    /// through one.
    /// </summary>
    public float WarningFraction
    {
        get => _warningFraction;
        set
        {
            _warningFraction = Math.Clamp(value, 0f, 1f);

            UpdateFill();
        }
    }

    /// <summary>
    /// Gets or sets the fraction at or below which <see cref="CriticalColor"/> is used. Clamped to zero
    /// through one.
    /// </summary>
    public float CriticalFraction
    {
        get => _criticalFraction;
        set
        {
            _criticalFraction = Math.Clamp(value, 0f, 1f);

            UpdateFill();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the fill switches to <see cref="WarningColor"/> and
    /// <see cref="CriticalColor"/> as the bar empties. Defaults to <see langword="false"/>.
    /// </summary>
    public bool UseThresholdColors
    {
        get => _useThresholdColors;
        set
        {
            _useThresholdColors = value;

            UpdateFill();
        }
    }

    /// <summary>
    /// Gets the world-pixel bounds of the bar's outer track.
    /// </summary>
    public Rectangle TrackBoundsWorld => _track.WorldBounds;

    /// <summary>
    /// Gets the world-pixel bounds of the bar's filled portion.
    /// </summary>
    public Rectangle FillBoundsWorld => _fill.WorldBounds;

    #endregion Properties

    #region Fluent members

    /// <summary>
    /// Sets the current value.
    /// </summary>
    /// <param name="value">The new value; it is clamped to zero through <see cref="MaxValue"/>.</param>
    /// <returns>This health bar, for chaining.</returns>
    public HealthBar SetValue(float value)
    {
        Value = value;

        return this;
    }

    /// <summary>
    /// Sets the fill colour used while the bar is above <see cref="WarningFraction"/>.
    /// </summary>
    /// <param name="color">The new fill colour.</param>
    /// <returns>This health bar, for chaining.</returns>
    public HealthBar SetFillColor(Color color)
    {
        FillColor = color;

        return this;
    }

    /// <summary>
    /// Sets the track's background and border colours.
    /// </summary>
    /// <param name="backgroundColor">The colour drawn behind the fill.</param>
    /// <param name="borderColor">The colour of the track's one-pixel inside border.</param>
    /// <returns>This health bar, for chaining.</returns>
    public HealthBar SetTrackColors(Color backgroundColor, Color borderColor)
    {
        _track.SetColor(backgroundColor).SetBorderColor(borderColor);

        return this;
    }

    /// <summary>
    /// Turns on threshold colouring and sets the two colours it uses.
    /// </summary>
    /// <param name="warningColor">The fill colour at or below <see cref="WarningFraction"/>.</param>
    /// <param name="criticalColor">The fill colour at or below <see cref="CriticalFraction"/>.</param>
    /// <returns>This health bar, for chaining.</returns>
    public HealthBar SetThresholdColors(Color warningColor, Color criticalColor)
    {
        _warningColor = warningColor;
        _criticalColor = criticalColor;
        _useThresholdColors = true;

        UpdateFill();

        return this;
    }

    /// <summary>
    /// Sets the fractions at which the warning and critical fill colours take over.
    /// </summary>
    /// <param name="warningFraction">The fraction at or below which the warning colour is used.</param>
    /// <param name="criticalFraction">The fraction at or below which the critical colour is used.</param>
    /// <returns>This health bar, for chaining.</returns>
    public HealthBar SetThresholds(float warningFraction, float criticalFraction)
    {
        _warningFraction = Math.Clamp(warningFraction, 0f, 1f);
        _criticalFraction = Math.Clamp(criticalFraction, 0f, 1f);

        UpdateFill();

        return this;
    }

    #endregion Fluent members

    #region Visibility and position

    /// <summary>
    /// Makes the bar visible. The filled portion stays hidden while <see cref="Value"/> is zero.
    /// </summary>
    /// <returns>This health bar, for chaining.</returns>
    public HealthBar Show()
    {
        SetIsVisible(true);
        UpdateFill();

        return this;
    }

    /// <summary>
    /// Hides the whole bar.
    /// </summary>
    /// <returns>This health bar, for chaining.</returns>
    public HealthBar Hide()
    {
        SetIsVisible(false);

        return this;
    }

    /// <summary>
    /// Re-centres the bar above the target's current draw location. It is called automatically whenever
    /// the target moves; call it after changing the target's size or alignment.
    /// </summary>
    public void RefreshPosition()
    {
        Rectangle targetBounds = Target.DrawLocationWorld;

        int x = targetBounds.Left + ((targetBounds.Width - _barSize.Width) / 2) + _offsetPx.X;
        int y = targetBounds.Top - _barSize.Height - DefaultGapPx + _offsetPx.Y;

        SetPosition(x, y);
    }

    #endregion Visibility and position

    #region Private members

    private void OnTargetMoved(SpriteMovedEventArgs args)
    {
        if (_disposed)
            return;

        RefreshPosition();
    }

    private void OnTargetDisposing(Sprite sprite)
    {
        Dispose();
    }

    private void UpdateFill()
    {
        if (_disposed)
            return;

        _fill.WorldBounds = GetFillBounds(_fill.WorldBounds.Location);

        Color resolved = ResolveFillColor();

        if (resolved != _appliedFillColor)
        {
            _appliedFillColor = resolved;
            _fill.SetColor(resolved);
        }

        _fill.Visible = _value > 0f && _track.Visible;
    }

    private Color ResolveFillColor()
    {
        if (!_useThresholdColors)
            return _fillColor;

        float fraction = Fraction;

        if (fraction <= _criticalFraction)
            return _criticalColor;

        return fraction <= _warningFraction
            ? _warningColor
            : _fillColor;
    }

    private Rectangle GetFillBounds(Point location)
    {
        int availableWidth = Math.Max(0, _barSize.Width - (InnerPadding * 2));
        int availableHeight = Math.Max(1, _barSize.Height - (InnerPadding * 2));
        int fillWidth = (int)MathF.Round(availableWidth * Fraction);

        return new Rectangle(location, new Size(fillWidth, availableHeight));
    }

    #endregion Private members

    #region Disposal

    /// <summary>
    /// Detaches the bar from its target and disposes its rectangles. Calling this more than once is safe.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Target.SpriteMoved -= OnTargetMoved;
        Target.Disposing -= OnTargetDisposing;

        base.Dispose();
    }

    #endregion Disposal
}
