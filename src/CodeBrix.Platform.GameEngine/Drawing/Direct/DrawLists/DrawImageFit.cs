namespace CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists; //CodeBrix (not from Gondwana)

/// <summary>How an image command fills its box.</summary>
public enum DrawImageFit
{
    /// <summary>The whole picture is drawn as large as fits inside the box, keeping its aspect ratio, centred.</summary>
    Contain = 0,

    /// <summary>The picture is stretched to the box exactly, ignoring its aspect ratio.</summary>
    Stretch = 1,
}
