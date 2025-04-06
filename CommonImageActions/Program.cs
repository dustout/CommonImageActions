using CommonImageActions.AspNetCore;
using CommonImageActions.Core;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var _env = app.Environment;

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/test",

    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/logos",
        DefaultImageActions = new ImageActions()
        {
            Height = 50,
            Width = 50,
            Format = SkiaSharp.SKEncodedImageFormat.Png
        }
    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/cached",
        UseDiskCache = true,
        DiskCacheLocation = Path.Join(_env.WebRootPath, "cache")
    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/two/deep"
    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/virtual",
        IsVirtual = true
    }
);

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/remote",
        RemoteFileServerUrl = "https://dustingamester.com/img/"
    }
);


app.UseStaticFiles();

app.MapGet("/", () => "Hello World!");

app.Run();
