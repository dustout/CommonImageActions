using CommonImageActions.AspNetCore;
using CommonImageActions.Core;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var _env = app.Environment;

//The disk cache is on unless it is turned off. It is turned off for most of this sample so that
//a change to the library shows up straight away instead of an image that was cached before the
//change. The /cached path further down shows the cache working.
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/test",
        UseDiskCache = false
    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/user/icons/",
        UseFileNameAsText = true,
        IsVirtual = true,
        UseDiskCache = false,
        DefaultImageActions = new ImageActions()
        {
            Height = 192,
            Width = 192,
            Format = SkiaSharp.SKEncodedImageFormat.Png,
            Shape = ImageShape.Circle,
            AsInitials = true,
            ChooseImageColorFromTextValue = true,
            Mode = ImageMode.Max
        }
    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/logos",
        UseDiskCache = false,
        DefaultImageActions = new ImageActions()
        {
            Height = 50,
            Width = 50,
            Format = SkiaSharp.SKEncodedImageFormat.Png
        }
    }
);

//example: https://localhost:44302/profilepicture/profile.png?t=DustinGa
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/profilepicture",
        IsVirtual = true,
        UseFileNameAsText = true,
        UseDiskCache = false,
        DefaultImageActions = new ImageActions()
        {
            Height = 100,
            Width = 100,
            Format = SkiaSharp.SKEncodedImageFormat.Png,
            Shape = ImageShape.Circle,
            AsInitials = true,
            ChooseImageColorFromTextValue = true,
        }
    }
);

//the disk cache as it is by default: on, in wwwroot/cache, and limited to 10 gigabytes.
//the location and the limit are only written out here to show how they are changed
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/cached",
        UseDiskCache = true,
        DiskCacheLocation = Path.Join(_env.WebRootPath, "cache"),
        DiskCacheMaxSizeBytes = CommonImageActionSettings.DefaultDiskCacheMaxSizeBytes
    }
);

//every image in this folder has the logo drawn in its corner
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/two/deep",
        UseDiskCache = false,
        DefaultImageActions = new ImageActions()
        {
            WatermarkImage = File.ReadAllBytes(Path.Join(_env.WebRootPath, "test", "logo.png")),
            WatermarkOpacity = 0.8
        }
    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/virtual",
        IsVirtual = true,
        UseDiskCache = false
    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/remote",
        RemoteFileServerUrl = "https://dustingamester.com/img/",
        UseDiskCache = false
    }
);

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
