# Common Image Actions
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/dustout/CommonImageActions/dotnet.yml)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/dustout/CommonImageActions/codeql.yml?label=Security%20Scan)
![NuGet Version](https://img.shields.io/nuget/v/CommonImageActions.Core)
![NuGet Downloads](https://img.shields.io/nuget/dt/CommonImageActions.Core)
![GitHub License](https://img.shields.io/github/license/dustout/CommonImageActions)
![GitHub Repo stars](https://img.shields.io/github/stars/dustout/CommonImageActions)

## Summary
A tuned image conversion library to add common image actions like resize and mask to your .Net 
and Asp.net core projects. This library is highly performant both in execution time and in memory use since it
extends Microsoft libraries. This library should continue to increase in performance as Microsoft further
improves their libraries.

![Animated gif that shows the functionality of common image actions](/CommonImageActions/wwwroot/test/ExplainerImage.gif)


 ## Code Samples

 ### Add to Asp.net Core
```csharp
app.UseCommonImageActions()
```

### Add to Asp.net Core and only watch a specific path
```csharp
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/watchme"
    }
);
```

### Add to Asp.net Core and route to an external blob storage
Like AzureBlob or Amazon S3
```csharp
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/remote",
        RemoteFileServerUrl = "https://dustingamester.com/img/"
    }
);
```