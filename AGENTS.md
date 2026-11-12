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
| `ImageActionQueryBuilder.cs` | Fluent wrapper around `ImageActions` (`ImageProcessor.Process(bytes).Width(100)...ToImageAsync()`). |
| `ImageMode.cs` | `Max`, `Fit`, `Zoom`, `Stretch` (Stretch is the default when mode is null). |
| `ImageShape.cs` | `Circle`, `Ellipse`, `RoundedRectangle`. |
| `ImageProcessorException.cs` | Thrown when decoding/encoding fails. |

Flow inside `ImageProcessor`:

1. Public entry points (`ProcessImageAsync` for `byte[]` or `Stream`, `ProcessImagesAsync` for a
   batch, `ProcessVirtualImageAsync`, and the fluent `Process`) all funnel into the internal
   `ProcessHelperAsync`.
2. `ProcessHelperAsync` runs on `Task.Run`. It either decodes the input with `SKCodec`, or, for a
   "virtual" image, creates a 100x100 solid-colour bitmap. An image is treated as virtual when the
   caller says so, or when `ImageColor` or `ChooseImageColorFromTextValue` is set.
3. `EncodeSkiaImage` (public, also called by the Pdf package) does everything else, in this order:
   resolve width/height, apply `Max` mode, clip to shape, rotate for EXIF orientation, draw the
   image according to mode (with optional grayscale colour filter), draw text/initials, pick the
   output format, encode. Drawing is recorded through an `SKPictureRecorder` and rasterised once.

Global tunables are public static fields on `ImageProcessor`: `JpegQuality`, `GifQuality`,
`CornerRadius`, `BackgroundColours` (the palette used for `ChooseImageColorFromTextValue`).

### CommonImageActions.Pdf

| File | Role |
| --- | --- |
| `PdfProcessor.cs` | Loads the PDF with PDFium, renders one page to a BGRA bitmap, wraps it as a BMP, decodes it with Skia, then hands it to `ImageProcessor.EncodeSkiaImage`. |
| `PdfActionQueryBuilder.cs` | Fluent wrapper, mirrors `ImageActionQueryBuilder` (it is a separate class, not a subclass). |
| `PdfProcessorException.cs` | Thrown when the PDF cannot be loaded or has no pages. |

PDF-specific options live on the shared `ImageActions` class (`Page`, 1-based, and `PdfPassword`).
An out-of-range page falls back to page 1. All PDFium calls are serialised behind one static
`Mutex`, so PDF rendering is effectively single-threaded per process.

### CommonImageActions.AspNetCore

| File | Role |
| --- | --- |
| `CommonImageActionsMiddlewareExtensions.cs` | `app.UseCommonImageActions(settings)`. Each call registers one middleware instance for one watched path. |
| `CommonImageActionSettings.cs` | Per-instance settings (`PathToWatch`, `IsVirtual`, `RemoteFileServerUrl`, `UseDiskCache`, `DiskCacheLocation`, `DefaultImageActions`, `UseFileNameAsText`) plus process-wide statics (`MaxUrlWidth`, `MaxUrlHeight`, `ValidImageExtensions`, `DefaultDiskCacheLocation`). |
| `CommonImageActionsMiddleware.cs` | Request handling and query-string parsing. |

Request flow in `InvokeAsync`:

1. Pass through unless the path starts with `PathToWatch` and ends in one of `ValidImageExtensions`.
2. Build `ImageActions` from `DefaultImageActions` + the query string
   (`ConvertQueryStringToImageActions`, public static, holds the long/short parameter names).
3. If disk cache is on and a cached file exists, stream it and stop. The cache key is the hex
   encoding of path + query string; there is no expiry or invalidation.
4. Pick a source: remote server (`RemoteFileServerUrl`), virtual (`IsVirtual`), or a local file
   under `<ContentRootPath>/wwwroot`.
5. For local files, fall through to the next middleware when the file is missing or when
   `ImageActions.HasAnyActions()` is false, so plain requests are served by `UseStaticFiles`.
6. Process via `PdfProcessor` (for `.pdf`) or `ImageProcessor`, write the response, then write the
   cache file if enabled.

The middleware must be registered before `app.UseStaticFiles()`.

### Tests

- xUnit, one test project per package. `dotnet test` at the repo root runs all three.
- Test assets are embedded through `Properties/Resources.resx` and read as
  `Properties.Resources.testJpg` / `testPdf`. The files live in
  `CommonImageActions.Core.Tests/TestImages/` and `CommonImageActions.Pdf.Tests/TestData/`.
  To add an asset, add the file and a `ResXFileRef` entry, and update `Resources.Designer.cs`.
- `CommonImageActions.Core.Tests/TestHelpers.cs` has `IsImage(byte[])`. The Pdf and AspNetCore
  test projects reference the Core test project to reuse it.
- Middleware tests spin up an in-memory host with `Microsoft.AspNetCore.TestHost`.
- Most assertions only check that the output decodes as an image. There are no pixel or
  dimension comparisons, so visual regressions need a manual check in the sample app.

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
  `/profilepicture`, `/cached` (disk cache), `/two/deep`, `/virtual`, `/remote`.
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
- **`EncodeSkiaImage` mutates the `ImageActions` it is given** (it writes the resolved `Width`,
  `Height`, `Shape` and `Format` back). The middleware relies on this to set the response
  content type. Do not reuse one instance where the resolved values of one image would be wrong
  for the next.
- **The middleware reports problems with `Console.WriteLine`** (cache read/write failures); it
  has no `ILogger` dependency.

## Adding a new image action

A new option has to be threaded through several places. Miss one and it will work from one entry
point but not another.

1. `CommonImageActions.Core/ImageActions.cs`: add the nullable property, copy it in the
   `ImageActions(ImageActions defaults)` constructor, and include it in `HasAnyActions()`.
   If it is left out of `HasAnyActions()`, a URL carrying only that parameter is passed straight
   to static files unprocessed.
2. `CommonImageActions.Core/ImageProcessor.cs`: apply it in `EncodeSkiaImage`.
3. `CommonImageActions.Core/ImageActionQueryBuilder.cs` and
   `CommonImageActions.Pdf/PdfActionQueryBuilder.cs`: add the fluent method to both.
4. `CommonImageActions.AspNetCore/CommonImageActionsMiddleware.cs`: parse the long and short
   query-string names in `ConvertQueryStringToImageActions`.
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
