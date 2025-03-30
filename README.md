# Common Image Actions

## About the Project
A c# library that helps with common image actions such as 
 - Resizing
 - Converting image to pdf
 - Resizing an image from a url

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