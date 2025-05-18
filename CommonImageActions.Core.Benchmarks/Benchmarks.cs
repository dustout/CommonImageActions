using BenchmarkDotNet;
using BenchmarkDotNet.Attributes;
using CommonImageActions.Core.Tests;
using Iced.Intel;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace CommonImageActions.Core.Benchmarks
{

    // * Summary *
    //BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.3915)
    //13th Gen Intel Core i7-13700K, 1 CPU, 24 logical and 16 physical cores
    //.NET SDK 9.0.300
    //  [Host]     : .NET 8.0.16 (8.0.1625.21506), X64 RyuJIT AVX2[AttachedDebugger]
    //  Job-SDJYSC : .NET 9.0.5 (9.0.525.21509), X64 RyuJIT AVX2

    //Platform = X64  Runtime=.NET 9.0

    //| Method                | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated | Alloc Ratio |
    //|---------------------- |----------:|---------:|---------:|------:|--------:|----------:|----------:|----------:|----------:|------------:|
    //| ConvertSingleImage    |  61.74 ms | 0.382 ms | 0.358 ms |  1.00 |    0.01 |  444.4444 |  444.4444 |  444.4444 |   1.96 MB |        1.00 |
    //| ConvertMultipleImages | 249.28 ms | 4.843 ms | 6.629 ms |  4.04 |    0.11 | 1000.0000 | 1000.0000 | 1000.0000 |  36.55 MB |       18.65 |

    public class Benchmarks
    {
        [Benchmark(Baseline = true)]
        public async Task ConvertSingleImage()
        {
            var unitTests = new ImageProcessorTests();
            await unitTests.ProcessImageAsync_ShouldReturnProcessedImage();
        }

        [Benchmark]
        public async Task ConvertMultipleImages()
        {
            var unitTests = new ImageProcessorTests();
            await unitTests.ProcessImagesAsync_ShouldReturnProcessedImages();
        }
    }
}
