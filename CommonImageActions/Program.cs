using CommonImageActions.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/test"
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
        PathToWatch = "/remote",
        RemoteFileServerUrl = "https://dustingamester.com/img/"
    }
);

app.MapGet("/", () => "Hello World!");

app.Run();
