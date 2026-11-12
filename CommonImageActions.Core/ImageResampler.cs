namespace CommonImageActions.Core
{
    /// <summary>
    /// How pixels are calculated when an image is resized. These are the samplers skia provides.
    /// </summary>
    public enum ImageResampler
    {
        /// <summary>Use the nearest pixel. Fastest, and keeps hard edges (pixel art).</summary>
        NearestNeighbor = 1,

        /// <summary>Blend the four nearest pixels.</summary>
        Bilinear = 2,

        /// <summary>Bilinear with mipmaps, better than bilinear when making an image much smaller.</summary>
        Mipmap = 3,

        /// <summary>Bicubic. Slowest and best quality, this is the default.</summary>
        Bicubic = 4
    }
}
