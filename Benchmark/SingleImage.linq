<Query Kind="Program">
  <NuGetReference>CommonImageActions.AspNetCore</NuGetReference>
  <NuGetReference>Imageflow.AllPlatforms</NuGetReference>
  <NuGetReference>Imageflow.Net</NuGetReference>
  <NuGetReference>SixLabors.ImageSharp</NuGetReference>
  <Namespace>BenchmarkDotNet.Attributes</Namespace>
  <Namespace>SixLabors.ImageSharp.Processing</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
</Query>

#load "BenchmarkDotNet"

byte[] imageData = null;

void Main()
{
	RunBenchmark(); return;  // Uncomment this line to initiate benchmarking.
}

[Benchmark]
public async Task CommonImageActionsBenchmark()   // Benchmark methods must be public.
{
	var actions = new CommonImageActions.Core.ImageActions();
	actions.Height = 50;
	actions.Width = 50;
	actions.Format = SkiaSharp.SKEncodedImageFormat.Png;
	var pngImageData = await CommonImageActions.Core.ImageProcessor.ProcessImageAsync(imageData,actions);


	//verify that it does get png data
	//var imageData2 = File.ReadAllBytes(@"C:\Temp\jpeg422jfif.jpg");
	//var pngImageData = CommonImageActions.Core.ImageProcessor.ProcessImage(imageData2,actions);
	//Util.Image(pngImageData).Dump();
}

[Benchmark]
public async Task SixLaborsBenchmark() // Benchmark methods must be public.
{
	SixLabors.ImageSharp.Image img = SixLabors.ImageSharp.Image.Load(imageData);
	img.Mutate(x => x.Resize(50, 50));
	var ms = new MemoryStream();
	img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
	var pngImageData = ms.ToArray();

	//verify that it does get png data
	//var ms = new MemoryStream();
	//img.Save(ms,new SixLabors.ImageSharp.Formats.Png.PngEncoder());
	//var pngImageData = ms.ToArray();
	//Util.Image(pngImageData).Dump();
}

[Benchmark]
public async Task ImageFlowBenchmark() // Benchmark methods must be public.
{
	using (var job = new Imageflow.Fluent.ImageJob())
	{
		var resizedImage = job.Decode(imageData)
		  .ResizerCommands($"width={50}&height={50}&mode=stretch&format=png")
		  .EncodeToBytes(new Imageflow.Fluent.LodePngEncoder())
		  .Finish()
		  .InProcessAsync()
		  .Result;
		  
		  var pngImageData = resizedImage.First.TryGetBytes().Value.Array;
	}

//	using (var job = new Imageflow.Fluent.ImageJob())
//	{
//		var imageData2 = File.ReadAllBytes(@"C:\Temp\rotate90.jpg");
//		var resizedImage2 = job.Decode(imageData2)
//		  .ResizerCommands($"width={50}&height={50}&mode=stretch&format=png")
//		  .EncodeToBytes(new Imageflow.Fluent.MozJpegEncoder(80, true))
//		  .Finish()
//		  .InProcessAsync()
//		  .Result;
//
//		Util.Image(resizedImage2.First.TryGetBytes().Value.Array).Dump();
//	}
}



[GlobalSetup]
public void BenchmarkSetup()
{
	//imageData = File.ReadAllBytes(@"C:\Temp\bigJpeg.jpg");
	//imageData = File.ReadAllBytes(@"C:\Temp\jpeg422jfif.jpg");
	//imageData = File.ReadAllBytes(@"C:\Temp\test.pdf");
	//imageData = File.ReadAllBytes(@"C:\Temp\bigPdf.pdf");
	imageData = File.ReadAllBytes(@"C:\Temp\rotate90.jpg");
}