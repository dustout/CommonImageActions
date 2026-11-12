namespace CommonImageActions.Core
{
    /// <summary>
    /// A position on the image. Used to choose which part of an image is kept when it is cropped
    /// by zoom, where an image sits when it is fitted, and where text and watermarks are placed.
    /// </summary>
    public enum ImageAnchor
    {
        Center = 1,
        Top = 2,
        Bottom = 3,
        Left = 4,
        Right = 5,
        TopLeft = 6,
        TopRight = 7,
        BottomLeft = 8,
        BottomRight = 9
    }
}
