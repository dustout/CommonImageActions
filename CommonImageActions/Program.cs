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
        PathToWatch = "/abc",
    }
);

app.MapGet("/", () => "Hello World!");

app.Run();
