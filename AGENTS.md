# AGENTS.md

Guidance for AI coding agents working in this repository. For user-facing documentation
(install steps, URL parameters, settings tables) see [README.md](README.md).

## What this is

Common Image Actions is a .NET image conversion library published as three NuGet packages.
It resizes, masks (circle / ellipse / rounded rectangle), grayscales, re-encodes and draws text
on images, renders PDF pages to images, and exposes all of that through an ASP.NET Core
middleware driven by URL query strings (`/img/photo.jpg?w=50&m=zoom`).

All drawing is done with SkiaSharp. PDF rendering uses PDFium.

## Repository layout

```
CommonImageActions.sln
├── CommonImageActions.Core/              NuGet package: image processing (netstandard2.1)
├── CommonImageActions.Pdf/               NuGet package: PDF page -> image (netstandard2.1)
├── CommonImageActions.AspNetCore/        NuGet package: URL-driven middleware (netstandard2.1)
├── CommonImageActions.Core.Tests/        xUnit tests for Core (net8.0)
├── CommonImageActions.Pdf.Tests/         xUnit tests for Pdf (net8.0)
├── CommonImageActions.AspNetCore.Tests/  xUnit tests for the middleware, via TestServer (net8.0)
├── CommonImageActions.Core.Benchmarks/   BenchmarkDotNet console app (net8.0)
├── CommonImageActions.SampleAspnetCoreProject/   Demo web app (net8.0)
├── Benchmark/                            LINQPad scripts + result images used in the README
└── .github/workflows/                    dotnet.yml (build + test), codeql.yml (security scan)
```

Package dependency chain (each one references the one before it):

```
CommonImageActions.Core  <-  CommonImageActions.Pdf  <-  CommonImageActions.AspNetCore
```

So the AspNetCore package always pulls in Pdf and Core; Core has no knowledge of the other two.

## Projects in detail

### CommonImageActions.Core

| File | Role |
| --- | --- |
| `ImageProcessor.cs` | Static entry point and the whole rendering pipeline. |
| `ImageActions.cs` | Plain options object describing what to do to an image. Every property is nullable; null means "not requested". |
| `ImageActionsBuilder.cs` | Generic base class holding every fluent option method (`Width`, `Crop`, `Rotate`, `Blur`, ...). Both builders inherit it, so they always offer the same options. |
| `ImageActionQueryBuilder.cs` | Fluent builder for images (`ImageProcessor.Process(bytes).Width(100)...ToImageAsync()`). Only holds the image data and the `ToImage`/`ToImageAsync`/`ToStreamAsync` methods. |
| `ImageMode.cs` | `Max`, `Fit`, `Zoom`, `Stretch` (Stretch is the default when mode is null). |
| `ImageShape.cs` | `Circle`, `Ellipse`, `RoundedRectangle`. |
| `ImageAnchor.cs` | Nine positions (`Center`, `Top`, `BottomRight`, ...). Used for zoom/fit placement, text and watermarks. |
| `ImageResampler.cs` | `NearestNeighbor`, `Bilinear`, `Mipmap`, `Bicubic`. Maps onto Skia's `SKFilterQuality`. |
| `ImageDetails.cs` | Result of `ImageProcessor.Identify` (size, format, orientation, frame count). |
| `JpegMetadata.cs` | Internal. Copies the EXIF/XMP/IPTC segments of a source JPEG into an output JPEG for `KeepMetadata`, resetting the orientation tag. |
| `ImageProcessorException.cs` | Thrown when decoding/encoding fails. |

Flow inside `ImageProcessor`:

1. Public entry points (`ProcessImageAsync` for `byte[]` or `Stream`, `ProcessImagesAsync` for a
   batch, `ProcessVirtualImageAsync`, and the fluent `Process`) all funnel into the internal
   `ProcessHelperAsync`.
2. `ProcessHelperAsync` runs on `Task.Run`. It either decodes the input with `SKCodec`, or, for a
   "virtual" image, creates a 100x100 solid-colour bitmap. An image is treated as virtual when the
   caller says so, or when `ImageColor` or `ChooseImageColorFromTextValue` is set.
3. `EncodeSkiaImage` (public, also called by the Pdf package) does everything else, in this order:
   1. Copy the actions.
   2. Work out the **source** size: EXIF orientation, then crop, then the box a rotation needs.
   3. Resolve width/height, apply `Max` mode, then `AllowUpscale = false`.
   4. Paint the background colour, then clip to the shape.
   5. Place the picture (`GetScaledRect`: mode, anchor, focal point) and draw it (`DrawSource`:
      crop, flip, rotate, EXIF) with the colour-matrix filter and resampler on the paint.
   6. Vignette, then text (`DrawText`), then watermark (`DrawWatermark`).
   7. Pick the output format and quality, encode.

   Drawing is recorded through an `SKPictureRecorder` and rasterised once. `ProcessHelperAsync`
   then splices in the source metadata if `KeepMetadata` is set.

Things to know about that pipeline:

- **Order of geometry is fixed:** EXIF orientation, crop, flip, rotate, resize. Crop values are in
  pixels of the displayed (EXIF-oriented) original.
- **Colour changes are one matrix.** `GetColorMatrix` multiplies saturation, grayscale, sepia,
  brightness, contrast and invert (in that order) into a single 4x5 matrix. The fifth column is
  in 0 to 1 units, not 0 to 255.
- **Blur and sharpen need an intermediate surface.** Skia's CPU blur treats everything outside
  the layer as transparent, which fades the edges. So the picture is drawn to an output-sized
  raster surface and repainted through the filter as an edge-clamped shader. This is the only
  path that allocates a full raster of the output; everything else stays in the recorded picture.
  Pixelate uses a small surface (output size divided by the block size).
- **Pixels are decoded into sRGB** (`DecodeBitmap`). Without that a wide-gamut photo (Display P3)
  has its numbers read as sRGB and comes out washed out. Output carries no colour profile.
- **Metadata is dropped by default.** `KeepMetadata` only works JPEG to JPEG, because Skia cannot
  write metadata and the segments are copied at the byte level.
- **Fonts:** text uses `FontFamily` (default Arial). `ImageProcessor.RegisterFont` keeps a
  typeface in a static dictionary for the life of the process; registered typefaces are shared
  and must not be disposed, system ones are looked up and disposed per image.
- **Skia limits that cannot be worked around:** only Nearest/Bilinear/Mipmap/Bicubic resampling
  (no Lanczos in 2.88), no animated output (only the first frame of a GIF is read), and no
  TIFF or TGA support.

- Sizes are calculated from the **displayed** size. A photo stored as 2048x1536 with an EXIF
  quarter turn is treated as 1536x2048. `DrawImageWithOrientation` handles all eight EXIF
  orientations and restores the canvas, so text drawn afterwards is always upright.
- Skia can only **write** JPEG, PNG and WebP. Any other requested or source format (GIF, BMP,
  ICO, ...) is encoded as PNG instead of failing.
- Bad input throws `ImageProcessorException` (undecodable data, a width or height of zero or
  less) or `ArgumentNullException` (null data or actions).
- Every `await` in Core and Pdf uses `ConfigureAwait(false)`. Keep it that way: the fluent
  `ToImage()` blocks on the async call and would deadlock a UI thread otherwise.

Global tunables are public static fields on `ImageProcessor`: `JpegQuality`, `GifQuality`,
`CornerRadius`, `BackgroundColours` (the palette used for `ChooseImageColorFromTextValue`).

Other public entry points: `Identify` (size and format from the header, no decode),
`RegisterFont`, and stream-to-stream `ProcessImageAsync(input, output, actions)`.

### CommonImageActions.Pdf

| File | Role |
| --- | --- |
| `PdfProcessor.cs` | Loads the PDF with PDFium, renders one page to a BGRA bitmap, lets Skia read that buffer in place (`SKImage.FromPixels`, no copy), then hands it to `ImageProcessor.EncodeSkiaImage`. |
| `PdfActionQueryBuilder.cs` | Fluent builder for PDFs. Inherits `ImageActionsBuilder<T>` from Core, so it has every option the image builder has. |
| `PdfProcessorException.cs` | Thrown when the PDF cannot be loaded or has no pages. |

PDF-specific options live on the shared `ImageActions` class (`Page`, 1-based, and `PdfPassword`).
An out-of-range page falls back to page 1. All PDFium calls are serialised behind one static
`Mutex`, so PDF rendering is effectively single-threaded per process.

The page is rendered at the size that was requested (`GetRenderScale`), never below its natural
72 dpi size, and never with a longest side above `PdfProcessor.MaxRenderDimension` (10000 px).
The PDFium bitmap must stay alive until `EncodeSkiaImage` returns, because Skia reads it directly.

### CommonImageActions.AspNetCore

| File | Role |
| --- | --- |
| `CommonImageActionsMiddlewareExtensions.cs` | `app.UseCommonImageActions(settings)`; each call registers one middleware instance for one watched path. Also `services.AddCommonImageActions(...)`, which registers the settings used when `UseCommonImageActions()` is called with none. |
| `CommonImageActionSettings.cs` | Per-instance settings (`PathToWatch`, `IsVirtual`, `RemoteFileServerUrl`, `UseFileNameAsText`, `DefaultImageActions`, the disk cache settings, `BrowserCacheMaxAge`, `SigningKey`, and the `OnActionsParsed`/`OnPrepareResponse` hooks) plus process-wide statics (`MaxUrlWidth`, `MaxUrlHeight`, `ValidImageExtensions`, `DefaultDiskCacheLocation`). |
| `CommonImageActionsMiddleware.cs` | Request handling, query-string parsing, HTTP caching. |
| `CommonImageActionsUrlSigner.cs` | Public. Creates and checks the HMAC-SHA256 `sig` parameter for signed urls. |
| `DiskCache.cs` | Internal. Reads and writes cached files (written to a temp name, then renamed), honours `DiskCacheMaxAge`, and trims the folder to `DiskCacheMaxSizeBytes`. |

Request flow in `InvokeAsync`:

1. Pass through unless the request is a GET or HEAD, the path starts with `PathToWatch`, and it
   ends in one of `ValidImageExtensions`.
2. If `SigningKey` is set and the request has a query string, the signature must be valid or
   the response is 403. A request with no query string needs no signature.
3. Build `ImageActions` from `DefaultImageActions` + the query string
   (`ConvertQueryStringToImageActions`, public static, holds the long/short parameter names).
   A query value only replaces a default when it is present in the url. Every numeric parameter
   has an allowed range (`TryGetNumber`); a value outside it is ignored, not clamped.
4. With `UseFileNameAsText`, the file name becomes the text unless the url has its own `text`.
5. Call `OnActionsParsed`, which may change the actions.
6. For local files, `GetLocalFilePath` resolves the decoded request path with `Path.GetFullPath`
   and returns null if it lands outside the web root. Fall through to the next middleware when
   it is null, the file is missing, or `ImageActions.HasAnyActions()` is false, so plain
   requests are served by `UseStaticFiles`.
7. Work out the cache file name (`GetCacheFileName`): a SHA-256 of the request path, the source
   file's last-write time and size, the library version, `JpegQuality`, and **every public
   property of `ImageActions`** (read by reflection). So `?w=50`, `?width=50` and
   `?w=50&unknown=1` share one name, and replacing the source file changes it.
8. For local and virtual images the ETag is the first 32 characters of that name. If the request's
   `If-None-Match` matches (or `If-Modified-Since` for local files), answer 304 with no work done.
9. Otherwise use the disk cache if it has the file (and it is younger than `DiskCacheMaxAge`),
   or get the source (remote via one shared static `HttpClient`, virtual, or the local file)
   and process it with `PdfProcessor` (for `.pdf`) or `ImageProcessor`. Write the cache file.
10. For remote images the ETag is a hash of the result, checked here (saves the download only).
11. Send `ETag`, `Last-Modified` (local files), `Cache-Control` (only when `BrowserCacheMaxAge`
    is set), `Content-Type`, `Content-Length`, call `OnPrepareResponse`, then the body (not for HEAD).

The response content type is always worked out from the first bytes of the data being sent
(`GetContentType`), for fresh, cached and remote pass-through responses alike.

Signed urls: what is signed is the unescaped path (including `PathBase`) plus the raw query
string before `&sig=`. The signature must be the last parameter. Changing how either side builds
that string breaks every url already written into pages.

Errors are logged through `ILogger` (resolved from an optional `ILoggerFactory` constructor
argument), falling back to `Console.WriteLine` when there is no logging.

Work with `context.Request.Path.Value` (already decoded) when touching path handling. Do not
rebuild a `Uri` from the request: that re-escapes the path, which is why file names with spaces
used to be missed. Any change to `GetLocalFilePath` needs the traversal tests to stay green.

The middleware must be registered before `app.UseStaticFiles()`.

### Tests

- xUnit, one test project per package. `dotnet test` at the repo root runs all three.
- Test assets are embedded through `Properties/Resources.resx` and read as
  `Properties.Resources.testJpg` / `testPdf`. The files live in
  `CommonImageActions.Core.Tests/TestImages/` and `CommonImageActions.Pdf.Tests/TestData/`.
  To add an asset, add the file and a `ResXFileRef` entry, and update `Resources.Designer.cs`.
- `CommonImageActions.Core.Tests/TestHelpers.cs` has `IsImage`, `CreateImage` (a solid colour
  image of a given size and format), `CreateQuadrantImage` (four coloured blocks, so a test can
  tell what a crop, flip or rotation did), `GetImageInfo`, `GetPixel`, `AssertColor` and
  `LoadTestImage`. The Pdf and AspNetCore test projects reference the Core test project to
  reuse them.
- Two tests use reflection over `ImageActions` so a new property cannot be half-added:
  `CopyConstructor_ShouldCopyEveryProperty` and `GetCacheFileName_ShouldChange_WhenAnyPropertyChanges`.
  A property of a new type needs a sample value added to `TestHelpers.CreateValue`.
- Text tests return early when no text was drawn, so they pass on a machine with no fonts.
- The EXIF test photos (`rotate90.jpg`, `rotate180.jpg`) are not duplicated: the Core test
  csproj links them from the sample app's `wwwroot/test/` into the test output folder.
- Middleware tests spin up an in-memory host with `Microsoft.AspNetCore.TestHost`.
  `CommonImageActions.AspNetCore.Tests/TestSite.cs` creates a throw-away site in the temp
  folder (with a file outside `wwwroot` for traversal tests) and can start a real Kestrel
  file server on a free local port to stand in for a remote file server.
- The `*RegressionTests.cs`, `*FeatureTests.cs` and `*RequestTests.cs` files assert sizes,
  formats, headers and individual pixels. The older tests only check that the output decodes as an image. Nothing
  compares whole images, so visual changes still need a look in the sample app.

### Benchmarks

- `CommonImageActions.Core.Benchmarks/` is a BenchmarkDotNet app whose benchmarks call the Core
  unit test methods directly. `BenchmarkConfig.cs` pins the job to the .NET 9 runtime on x64, so
  the .NET 9 SDK/runtime must be installed to run it even though the project targets net8.0.
- `Benchmark/*.linq` are LINQPad scripts that compare the published NuGet package against
  ImageSharp and Imageflow. The PNGs beside them are the result screenshots shown in the README.

### Sample app

`CommonImageActions.SampleAspnetCoreProject/` (note: the csproj inside is named
`CommonImageActions.SampleAspNetProject.csproj`).

- `Program.cs` registers one middleware per scenario: `/test`, `/user/icons`, `/logos`,
  `/profilepicture`, `/cached` (disk cache), `/two/deep` (watermark on every image), `/virtual`,
  `/remote`.
- `wwwroot/index.html` is a demo page exercising the URL parameters.
- `wwwroot/test/` holds sample inputs (EXIF-rotated JPEGs, chroma-subsampling variants, PDFs).
  It also holds `logo.png`, which is packed as the NuGet icon, and the images the README links to
  on GitHub, so do not rename or move those files.
- `UnreachableFolder/` sits outside `wwwroot` and exists to check that path traversal is blocked.
- `wwwroot/cache/` is generated at runtime and git-ignored.

## Commands

Run from the repository root.

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet test CommonImageActions.Core.Tests
```

```bash
dotnet run --project CommonImageActions.SampleAspnetCoreProject
```

```bash
dotnet run -c Release --project CommonImageActions.Core.Benchmarks
```

The sample app listens on `http://localhost:5259` (and `https://localhost:7206` with the https
profile).

CI (`.github/workflows/dotnet.yml`) runs restore, build and test on `ubuntu-latest` with .NET 8
for pushes and pull requests against `master`. Changes must therefore work on Linux as well as
Windows.

## Conventions and constraints

- **Library projects target `netstandard2.1`.** That means C# 8 by default: no file-scoped
  namespaces, records, `init` setters, or global usings in the three package projects. Test,
  benchmark and sample projects are net8.0 with implicit usings.
- **Nullable reference types** are enabled in `CommonImageActions.Pdf` and the net8.0 projects,
  and disabled in `Core` and `AspNetCore`.
- **SkiaSharp is 2.88.x**, pulled in through `Microsoft.Maui.Graphics.Skia`. The code uses 2.88
  APIs such as `SKFilterQuality` and `SKPaint.TextSize`; do not write against SkiaSharp 3.x.
- **Dispose every SkiaSharp object deterministically** (`using var`). They hold native memory the
  GC cannot see. `EncodeSkiaImage` returns an `SKData` that the caller must dispose.
- **Code style:** block-scoped namespaces, Allman braces, 4-space indent, `var` for locals,
  short lower-case `//comments`. There is no `.editorconfig` or analyzer config; match the
  surrounding file.
- **Colours are hex strings without `#`** (`"ffccff"`), on both the API and the query string.
- **Text is drawn with the "Arial" family.** On Linux, Skia substitutes whatever font is
  available, so text metrics differ across platforms.
- **`EncodeSkiaImage` must not modify the `ImageActions` it is given.** It works on a copy made
  with the copy constructor, because one instance is routinely shared between images
  (`ProcessImagesAsync`, `DefaultImageActions`). Writing a calculated size or format back would
  apply it to the next image.
- **Anything a url can set must be bounded.** Give each new numeric query parameter a range in
  `ConvertQueryStringToImageActions`, sized so that one request cannot tie up the server.

## Adding a new image action

A new option has to be threaded through several places. Miss one and it will work from one entry
point but not another.

1. `CommonImageActions.Core/ImageActions.cs`: add the nullable property, copy it in the
   `ImageActions(ImageActions defaults)` constructor, and include it in `HasAnyActions()` if it
   changes the image on its own. If it is left out of `HasAnyActions()`, a URL carrying only that
   parameter is passed straight to static files unprocessed. If it is left out of the copy
   constructor it is silently dropped, because `EncodeSkiaImage` and the middleware both work on
   copies (a test fails if you forget).
2. `CommonImageActions.Core/ImageProcessor.cs`: apply it in `EncodeSkiaImage`.
3. `CommonImageActions.Core/ImageActionsBuilder.cs`: add the fluent method. Both the image and
   the PDF builder get it from there.
4. `CommonImageActions.AspNetCore/CommonImageActionsMiddleware.cs`: parse the long and short
   query-string names in `ConvertQueryStringToImageActions`, with a range for numbers. Check the
   short name is not already taken. The cache key picks the property up by itself.
5. `README.md`: add a row to the URL Parameters table.
6. Add tests, and a sample to `wwwroot/index.html` in the sample app if it is visual.

## Releasing

- The version number is duplicated in the `<Version>` element of all three package csproj files
  (`Core`, `Pdf`, `AspNetCore`). Bump them together.
- `GeneratePackageOnBuild` is on, so every build writes `.nupkg` and `.snupkg` files to each
  package project's `bin/<Configuration>/` folder. They are git-ignored.
- Each package embeds the root `README.md`, `LICENSE.txt` and
  `CommonImageActions.SampleAspnetCoreProject/wwwroot/test/logo.png`.
- Branch names follow `feature/YYYYMMDD-short-description`, `bugfix/YYYYMMDD-short-description`,
  and `vX.Y.Z` for release branches. Everything reaches `master` by pull request.
- Releases are tagged `X.Y.Z` (no `v` prefix). Nothing in the repo publishes to NuGet
  automatically.
