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


## Getting Started (Asp.Net Core)
#### Install the NuGet Package
```bash
    Install-Package CommonImageActions.AspNetCore
```


#### Add UseCommonImageActions Middleware
Add the middleware to your startup file. Make sure that it is above `app.UseStaticFiles()`. The default implementation 
will watch for all supported image files based on the file extension.
```csharp
app.UseCommonImageActions();
```

You can watch a specific directory by setting the `PathToWatch` property in the `CommonImageActionSettings` object.
```csharp
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/test",
    }
);
```


## Getting Started
#### Install the NuGet Package
```bash
    Install-Package CommonImageActions.Core
```

#### Pass image bytes to the ImageProcessor.ProcessImageAsync 
Get the image `bytes[]` and pass those bytes along with imageActions to the `ImageProcessor.ProcessImageAsync` function
```csharp
var imageData = File.ReadAllBytes("test.png"); //read in your image data
var actions = new CommonImageActions.Core.ImageActions();
actions.Height = 50;
actions.Width = 50;
actions.Format = SkiaSharp.SKEncodedImageFormat.Png;
var pngImageData = await CommonImageActions.Core.ImageProcessor.ProcessImageAsync(imageData,actions);
```




 ## Common Code Examples (Asp.net core)

 #### Watch all images in all directories
```csharp
app.UseCommonImageActions()
```

#### Set all images to be 50x50 png images for the /logos directory
```csharp
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/logos",
        DefaultImageActions = new CommonImageActions.Core.ImageActions()
        {
            Height = 50,
            Width = 50,
            Format = SkiaSharp.SKEncodedImageFormat.Png
        }
    }
);
```

#### Cache generate profile pictures and cache the response to the disk
```csharp
//the ChooseImageColorFromTextValue ensures that the following will have a different background color
//https://localhost:44302/profilepicture/profile.png?t=DustinGa
//https://localhost:44302/profilepicture/profile.png?t=DustinG
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/profilepicture",
        IsVirtual = true,
        UseDiskCache = true,
        DefaultImageActions = new ImageActions()
        {
            Height = 50,
            Width = 50,
            Format = SkiaSharp.SKEncodedImageFormat.Png,
            Shape = ImageShape.Circle,
            AsInitials = true,
            ChooseImageColorFromTextValue = true
        }
    }
);
```

#### Add to Asp.net Core and route to an external blob storage
Like AzureBlob or Amazon S3 or any other remote server
```csharp
app.UseCommonImageActions(
    new CommonImageActionSettings()
    {
        PathToWatch = "/remote",
        RemoteFileServerUrl = "https://dustingamester.com/img/"
    }
);
```