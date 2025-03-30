using CommonImageActions.Core;
using System;

namespace CommonImageActions.AspNetCore
{
    public class CommonImageActionSettings
    {
        public string PathToWatch { get; set; } = "/";

        public string RemoteFileServerUrl { get; set; }

        public ImageActions DefaultImageActions { get; set; }

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
    }
}
