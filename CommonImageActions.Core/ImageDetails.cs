using SkiaSharp;

namespace CommonImageActions.Core
{
    /// <summary>
    /// What can be read from the header of an image without decoding its pixels.
    /// </summary>
    public class ImageDetails
    {
        /// <summary>
        /// Width of the image as it is displayed, after its exif orientation is applied.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Height of the image as it is displayed, after its exif orientation is applied.
        /// </summary>
        public int Height { get; set; }

        public SKEncodedImageFormat Format { get; set; }

        /// <summary>
        /// The exif orientation of the stored pixels.
        /// </summary>
        public SKEncodedOrigin Orientation { get; set; }

        /// <summary>
        /// Number of frames, more than one for an animated image.
        /// </summary>
        public int FrameCount { get; set; }
    }
}
