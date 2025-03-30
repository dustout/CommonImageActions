using CommonImageActions.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseUrlImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/test"
    }
);

app.UseUrlImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/abc"
    }
);

app.MapGet("/", () => "Hello World!");

app.Run();
