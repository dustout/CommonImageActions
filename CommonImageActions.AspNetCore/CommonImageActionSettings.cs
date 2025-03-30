using System;

namespace CommonImageActions.AspNetCore
{
    public class CommonImageActionSettings
    {
        public string PathToWatch { get; set; } = "/";

        public static string[] ValidImageExtensions = {
            ".bmp",
            ".gif",
            ".ico",
            ".jpg",
            ".jpeg",
            ".png",
            ".wbmp",
            ".webp",
            ".pkm",
            ".ktx",
            ".astc",
            ".dng",
            ".heif",
            ".avif",
            ".jpegxl",
            ".jxl",

            ".pdf"
        };

        public static int JpegQuality = 90;
        public static int GifQuality = 90;
    }
}
