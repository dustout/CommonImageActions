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

## Features
✅ Resize images<br>
✅ Convert images to be circle or rounded rectangle<br>
✅ PDF support<br>
✅ Fast and memory efficient<br>
✅ Create user profile placeholders where background auto changes based on name
<img src="/CommonImageActions/wwwroot/test/ProfilePictureStrip.png" alt="Example of user profile placeholders" style="height: 18px;">
<br>
✅ Resize through url in asp.net core (`.jpg?w=50&m=zoom`) <br>
✅ Works with any project that supports .net standard


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

#### Generate user placeholders and cache the response to the disk
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

## URL Parameters (Asp.net Core)
| Parameter | Possible Values | Description |
|  ------------- | ------------- | ------------- |
| width, w  | Integer | Set the width of the image |
| height, h | Integer | Set the height of the image |
| mode, m | Max, Fit, Zoom, Stretch | **Max**: If both width and height are present then the image will be resized to the max of whatever parameter, and the width will scale accordingly. <br> **Fit**: When both width and height are present fit the image into the container without adjusting the ratio.  <br> **Zoom**: When both width and height are present zoom the image in to fill the space. <br> **Stretch** (default): When both width and height are present stretch the image to fit the container. |
| shape, s | Circle, Ellipse, RoundedRectangle  | Mask out the image to be of a certain shape. |
| corner, cr | Integer | The corner radius when shape is RoundedRectangle. Default is 10. |
| text, t | String | The text to display on the image |
| initials, in | Boolean | When true will only display initials of text. For example DustinG is displayed as DG. |
| color, c | String (ffccff or blue) | Set a color for the image |
| textColor, tc | String (ffccff or blue) | Set the color of the text |
| colorFromText, ft | Boolean | When true a color will be generated based on a hash of the text. The list of colors can be updated in `ImageProcessor.BackgroundColours`. |
| format, f | Bmp, Gif, Ico, Jpeg, Png, Wbmp, Webp, Pkm, Ktx, Astc, Dng, Heif, Avif | What format to export the resulting image as. Default is png.  |
| password, pw | String | (pdf only) password to open pdf |
| page, p  | Integer | (pdf only) what page to render. First page is 1.|

#### Modes Visualized
![Visual that shows the different mode options](/CommonImageActions/wwwroot/test/ModeExplainer.png)


## CommonImageActionSettings
| Setting | Description |
|  ------------- | ------------- |
| PathToWatch  | What path to watch. For example `/test` will watch for images in the test directory |
| IsVirtual  | Avoid looking up the image and construct the image virtually (useful for profile pictures) |
| RemoteFileServerUrl  | The URL of the remote resource to pull images from. Often a blob storage like Amazon S3 or Azure Blob |
| UseDiskCache | When true the system will save and return cached images. This can dramatically improve performance. |
| DiskCacheLocation | Where to store and retrieve the DiskCache images from. This directory needs to be writable. If it is not the system will print errors to the console, but will still continue to run. |
| DefaultImageActions | Set a default image action to be used on all requests against a particular path. Useful when you want all images in a directory to be a specific dimension. |

## Benchmarking
Use [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) to compare the performance between 
[Common Image Actions](https://github.com/dustout/CommonImageActions), 
[ImageSharp](https://github.com/SixLabors/ImageSharp), and 
[ImageFlowDotNet](https://github.com/imazen/imageflow-dotnet).
The benchmark files can be found in the /Benchmark directory if you wish to run the same test. 

#### Single Image
When converting a single image we can see the the AllocatedBytes is half of ImageSharp, and the performance is faster than
ImageFlow <br>
![Benchmarking results of CommonImageActions against a single image](/Benchmark/SingleImageResults.png)

#### Multiple Images
When converting multiple images we can see where CommonImageActions really shines. What takes 547ms in CommonImageActions
takes 4,400ms in ImageSharp and 11,430ms in ImageFlow. We can also see that the allocated memory 
is significantly less than ImageSharp.
![Benchmarking results of CommonImageActions against multiple images](/Benchmark/MultipleImageResults.png)
