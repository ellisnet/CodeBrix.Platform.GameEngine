using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Options that steer how an <see cref="ITilesheetAssetSource"/> pre-renders a
/// <see cref="GameAssetKind.Model3D"/> asset into a <see cref="Tilesheet"/> of sprite frames, for
/// games that draw in two dimensions. Every member is optional; the defaults produce an
/// eight-direction sheet of the model's rest pose.
/// </summary>
/// <remarks>
/// <para>
/// This is the SPRITE route for a model asset, as opposed to the model-DATA route of
/// <see cref="IModelAssetSource"/>. The engine does not draw 3D; a provider renders the views
/// once, at load time, and the game then draws ordinary tiles.
/// </para>
/// <para>
/// OUTPUT LAYOUT CONTRACT — the produced sheet carries ONE uniform-grid
/// <see cref="TilesheetRegion"/> per rendered animation, named after that animation, plus a region
/// named <see cref="RestPoseRegionName"/> when <see cref="IncludeRestPose"/> is
/// <see langword="true"/>. Within a region:
/// </para>
/// <para>
/// COLUMNS are animation frames, in playback order, and there are
/// <see cref="AnimationFramesPerSecond"/> of them per second of the clip. The rest-pose region has
/// exactly one column.
/// </para>
/// <para>
/// ROWS are camera directions, and there are <see cref="Directions"/> of them. Row <c>d</c> shows
/// the model at the camera yaw <c>StartYawDegrees + d * 360 / Directions</c>. Yaw is the camera's
/// azimuth around the model's up axis (+Y): at yaw 0 the camera sits on the +Z side of the model
/// and looks toward the origin along -Z, so a model authored facing +Z — the glTF convention,
/// which <see cref="GameModelMesh"/> geometry follows — faces the viewer head-on. Increasing yaw
/// swings the camera toward the model's +X side, which makes the model appear to turn toward the
/// viewer's left; with the default eight directions, row 2 (yaw 90) therefore shows the model's
/// left flank and row 4 (yaw 180) its back.
/// </para>
/// <para>
/// IN SCREEN TERMS, which is what a game picks a row by: with the default eight directions and a
/// <see cref="StartYawDegrees"/> of 0, the model FACES toward the viewer (down-screen, "south") in
/// row 0, south-west in row 1, screen-left ("west") in row 2, north-west in row 3, away from the
/// viewer ("north") in row 4, north-east in row 5, screen-right ("east") in row 6 and south-east
/// in row 7 — so a character walking toward the right of the screen is drawn from row 6.
/// </para>
/// <para>
/// EVERY CELL is exactly <see cref="FrameSize"/>, so a frame is addressed by region name, column
/// and row: <c>sheet["walk", frame, direction]</c>, and the rest pose is
/// <c>sheet[ModelRenderOptions.RestPoseRegionName, 0, direction]</c>.
/// </para>
/// <para>
/// ONE COMMON SCALE fits the model to the cell across the whole sheet — every direction and every
/// frame of every region share it — so the model never appears to breathe or jump in size from one
/// cell to the next. <see cref="FitPadding"/> is the margin that fit leaves empty.
/// </para>
/// </remarks>
public sealed record ModelRenderOptions
{
    /// <summary>
    /// The name of the tilesheet region that holds the model's rest pose, one column wide.
    /// </summary>
    public const string RestPoseRegionName = "rest";

    private static readonly Vector3 DefaultLightDirection =
        Vector3.Normalize(new Vector3(-0.5f, 0.8f, 0.6f));

    /// <summary>
    /// Gets the size in pixels of one cell of the produced sheet. Defaults to 128 by 128.
    /// </summary>
    public Size FrameSize { get; init; } = new Size(128, 128);

    /// <summary>
    /// Gets the number of camera directions rendered, which is the number of rows of every region
    /// of the sheet. Defaults to <c>8</c>.
    /// </summary>
    public int Directions { get; init; } = 8;

    /// <summary>
    /// Gets the camera yaw of the first row, in degrees. Defaults to <c>0</c>, which looks at the
    /// front of a model authored facing +Z.
    /// </summary>
    public float StartYawDegrees { get; init; }

    /// <summary>
    /// Gets the camera elevation above the model's horizontal plane, in degrees, where <c>0</c> is
    /// level with the model and <c>90</c> looks straight down on it. Defaults to <c>30</c>, the
    /// three-quarter view that suits a sprite sheet.
    /// </summary>
    public float PitchDegrees { get; init; } = 30.0f;

    /// <summary>
    /// Gets the camera projection. Defaults to <see cref="ModelProjection.Orthographic"/>, which
    /// keeps the model the same size in every cell of the sheet.
    /// </summary>
    public ModelProjection Projection { get; init; } = ModelProjection.Orthographic;

    /// <summary>
    /// Gets the vertical field of view in degrees, used only when <see cref="Projection"/> is
    /// <see cref="ModelProjection.Perspective"/>. Defaults to <c>35</c>.
    /// </summary>
    public float FieldOfViewDegrees { get; init; } = 35.0f;

    /// <summary>
    /// Gets the names of the animations to render, each becoming one region of the sheet, or
    /// <see langword="null"/> to render the rest pose only. Names are matched case-insensitively
    /// against the animations the asset offers; an empty list also renders no animation.
    /// </summary>
    public IReadOnlyList<string>? AnimationNames { get; init; }

    /// <summary>
    /// Gets a value indicating whether the sheet also carries a one-column
    /// <see cref="RestPoseRegionName"/> region of the un-animated model. Defaults to
    /// <see langword="true"/>, so a sheet always has something to draw.
    /// </summary>
    public bool IncludeRestPose { get; init; } = true;

    /// <summary>
    /// Gets the rate the animations are sampled at, in frames per second, which decides how many
    /// columns an animation region has. Defaults to <c>12</c>, lower than the model-data default
    /// because every frame costs a cell of sheet area.
    /// </summary>
    public int AnimationFramesPerSecond { get; init; } = 12;

    /// <summary>
    /// Gets the factor each cell is rendered oversized by before it is scaled down into the sheet,
    /// which smooths the edges of the model. Defaults to <c>2</c>; <c>1</c> disables it.
    /// </summary>
    public int Supersample { get; init; } = 2;

    /// <summary>
    /// Gets the direction from the model TOWARD the light, in model space, as a unit vector.
    /// Defaults to a normalized direction from above, in front of and to the left of the camera at
    /// yaw 0, which gives a solid shape readable shading.
    /// </summary>
    public Vector3 LightDirection { get; init; } = DefaultLightDirection;

    /// <summary>
    /// Gets how much of a surface's colour survives where the light does not reach it, from
    /// <c>0</c> (unlit surfaces are black) to <c>1</c> (no shading at all). Defaults to
    /// <c>0.45</c>.
    /// </summary>
    public float AmbientLight { get; init; } = 0.45f;

    /// <summary>
    /// Gets the fraction of each cell kept empty around the model when it is fitted, from
    /// <c>0</c> (the model touches the cell edges) to just under <c>0.5</c>. Defaults to
    /// <c>0.06</c>.
    /// </summary>
    public float FitPadding { get; init; } = 0.06f;
}
