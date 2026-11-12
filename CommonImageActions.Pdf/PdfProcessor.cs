
using CommonImageActions.Core;
using Microsoft.Maui.Graphics.Skia;
using PDFiumCore;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CommonImageActions.Pdf
{
    public static class PdfProcessor
    {
        private static bool isPdfiumInitalized = false;

        private static Mutex mut = new Mutex();

        /// <summary>
        /// The longest side, in pixels, that a pdf page will be rendered at before the image actions
        /// are applied. Limits how much memory a single page can use.
        /// </summary>
        public static int MaxRenderDimension = 10000;

        public static PdfActionQueryBuilder Process(byte[] imageData)
        {
            return new PdfActionQueryBuilder(imageData);
        }

        public static async Task<IEnumerable<byte[]>> ProcessPdfsAsync(IEnumerable<byte[]> pdfsData, ImageActions actions)
        {
            var returnList = new List<byte[]>();

            var activeJobs = new List<Task<byte[]>>();
            foreach (var pdfData in pdfsData)
            {
                var job = ProcessPdfAsync(pdfData, actions);
                activeJobs.Add(job);
            }

            await Task.WhenAll(activeJobs).ConfigureAwait(false);

            //by going through it this way we keep the order the same as it came in
            foreach (var completeJob in activeJobs)
            {
                returnList.Add(completeJob.Result);
            }

            return returnList;
        }

        public async static Task<byte[]> ProcessPdfAsync(byte[] imageData, ImageActions actions)
        {
            return await ProcessHelperAsync(imageData, actions).ConfigureAwait(false);
        }

        public async static Task<byte[]> ProcessPdfAsync(Stream imageStream, ImageActions actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            //copy stream into memory asyncronously
            byte[] imageData = null;
            using (var ms = new MemoryStream())
            {
                await imageStream.CopyToAsync(ms).ConfigureAwait(false);
                imageData = ms.ToArray();
            }

            return await ProcessHelperAsync(imageData, actions).ConfigureAwait(false);
        }

        /// <summary>
        /// Render a page of a pdf from one stream and write the resulting image to another.
        /// </summary>
        public async static Task ProcessPdfAsync(Stream imageStream, Stream outputStream, ImageActions actions)
        {
            if (outputStream == null)
            {
                throw new ArgumentNullException(nameof(outputStream));
            }

            var result = await ProcessPdfAsync(imageStream, actions).ConfigureAwait(false);
            await outputStream.WriteAsync(result, 0, result.Length).ConfigureAwait(false);
        }

        internal static async Task<byte[]> ProcessHelperAsync(byte[] pdfData, ImageActions actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            if (pdfData == null)
            {
                throw new ArgumentNullException(nameof(pdfData));
            }

            byte[] returnValue = null;

            //make sure pdfium is initalized
            if (isPdfiumInitalized == false)
            {
                mut.WaitOne();

                //double check if another thread has already initialized it
                if (isPdfiumInitalized == false)
                {
                    //initialize pdfium
                    fpdfview.FPDF_InitLibrary();
                    isPdfiumInitalized = true;
                }

                mut.ReleaseMutex();
            }

            await Task.Run(() =>
            {
                var handle = GCHandle.Alloc(pdfData, GCHandleType.Pinned);
                try
                {
                    mut.WaitOne();
                    var pdfDocument = fpdfview.FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), pdfData.Length, actions.PdfPassword);
                    if (pdfDocument == null)
                    {
                        throw new PdfProcessorException("Error loading pdf");
                    }

                    try
                    {
                        //make sure there is at least one page
                        var pageCount = fpdfview.FPDF_GetPageCount(pdfDocument);
                        if (pageCount == 0)
                        {
                            throw new PdfProcessorException("Error getting pdf page count");
                        }

                        //set requested page, if not requested default to first page
                        //also offset by 1 for usability to match readers
                        var requestedPage = 0;
                        if (actions.Page.HasValue && actions.Page <= pageCount && actions.Page > 0)
                        {
                            requestedPage = actions.Page.Value - 1;
                        }

                        //get the first page
                        var pdfPage = fpdfview.FPDF_LoadPage(pdfDocument, requestedPage);
                        if (pdfPage == null)
                        {
                            throw new PdfProcessorException($"Error loading pdf page {requestedPage}");
                        }

                        try
                        {
                            //get dimensions of the page, these are in points (1/72 of an inch)
                            var pageWidth = (double)fpdfview.FPDF_GetPageWidth(pdfPage);
                            var pageHeight = (double)fpdfview.FPDF_GetPageHeight(pdfPage);
                            if (pageWidth <= 0 || pageHeight <= 0)
                            {
                                throw new PdfProcessorException("Error getting pdf page size");
                            }

                            //render the page at the size that was asked for, so a large image is drawn
                            //sharp by pdfium instead of being a small render that is stretched afterwards
                            var renderScale = GetRenderScale(pageWidth, pageHeight, actions);
                            var pdfWidth = Math.Max(1, (int)(pageWidth * renderScale));
                            var pdfHeight = Math.Max(1, (int)(pageHeight * renderScale));

                            //create a bitmap of the page
                            var pdfBitmap = fpdfview.FPDFBitmapCreate(pdfWidth, pdfHeight, 0);
                            if (pdfBitmap == null)
                            {
                                throw new PdfProcessorException("Error creating bitmap for pdf page");
                            }

                            try
                            {
                                fpdfview.FPDFBitmapFillRect(pdfBitmap, 0, 0, pdfWidth, pdfHeight, 0xFFFFFFFF);
                                fpdfview.FPDF_RenderPageBitmap(pdfBitmap, pdfPage, 0, 0, pdfWidth, pdfHeight, 0, 0);

                                //get handle to buffer
                                var buffer = fpdfview.FPDFBitmapGetBuffer(pdfBitmap);
                                var stride = fpdfview.FPDFBitmapGetStride(pdfBitmap);

                                //let skia read the pdfium buffer where it is rather than copying it. The
                                //buffer stays alive until FPDFBitmapDestroy below, after the encode is done
                                var imageInfo = new SKImageInfo(pdfWidth, pdfHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
                                using var newImage = SKImage.FromPixels(imageInfo, buffer, stride);
                                if (newImage == null)
                                {
                                    throw new PdfProcessorException("Error reading rendered pdf page");
                                }

                                //process skia image into encoded image
                                using var encoded = ImageProcessor.EncodeSkiaImage(newImage, actions);
                                if (encoded == null)
                                {
                                    throw new PdfProcessorException("Error encoding pdf page");
                                }

                                returnValue = encoded.ToArray();
                            }
                            finally
                            {
                                fpdfview.FPDFBitmapDestroy(pdfBitmap);
                            }
                        }
                        finally
                        {
                            fpdfview.FPDF_ClosePage(pdfPage);
                        }
                    }
                    finally
                    {
                        fpdfview.FPDF_CloseDocument(pdfDocument);
                    }
                }
                finally
                {
                    mut.ReleaseMutex();
                    handle.Free();
                }
            }).ConfigureAwait(false);

            return returnValue;
        }

        /// <summary>
        /// How much larger than its natural size (72 dpi) the page needs to be rendered so that the
        /// requested width and height are covered without stretching the render afterwards.
        /// </summary>
        public static double GetRenderScale(double pageWidth, double pageHeight, ImageActions actions)
        {
            double scale = 1;

            var hasWidth = actions != null && actions.Width.HasValue && actions.Width.Value > 0;
            var hasHeight = actions != null && actions.Height.HasValue && actions.Height.Value > 0;

            if (hasWidth && hasHeight)
            {
                var widthScale = actions.Width.Value / pageWidth;
                var heightScale = actions.Height.Value / pageHeight;

                //fit and max keep the whole page inside the requested box, the other modes cover it
                if (actions.Mode == ImageMode.Fit || actions.Mode == ImageMode.Max)
                {
                    scale = Math.Min(widthScale, heightScale);
                }
                else
                {
                    scale = Math.Max(widthScale, heightScale);
                }
            }
            else if (hasWidth)
            {
                scale = actions.Width.Value / pageWidth;
            }
            else if (hasHeight)
            {
                scale = actions.Height.Value / pageHeight;
            }

            //never render below the natural size, a thumbnail looks better scaled down from it
            scale = Math.Max(1, scale);

            //keep the render to a sane size so one request can not use an unbounded amount of memory
            var longestSide = Math.Max(pageWidth, pageHeight);
            if (MaxRenderDimension > 0 && longestSide * scale > MaxRenderDimension)
            {
                scale = MaxRenderDimension / longestSide;
            }

            return scale;
        }

        //no longer used when rendering a page (skia reads the pdfium buffer directly), kept because it is public
        public static byte[] ConvertFromBGRA32ToBmp(byte[] managedArray, int width, int height)
        {
            int bytesPerPixel = 4; // BGRA32

            // Calculate the size of the image data
            int imageSize = width * height * bytesPerPixel;

            // Create BMP file header (14 bytes)
            byte[] bmpFileHeader = new byte[14];
            bmpFileHeader[0] = (byte)'B';
            bmpFileHeader[1] = (byte)'M';
            int fileSize = 54 + imageSize; // 54 bytes for headers + image data
            BitConverter.GetBytes(fileSize).CopyTo(bmpFileHeader, 2);
            bmpFileHeader[10] = 54; // Pixel data offset

            // Create DIB header (40 bytes)
            byte[] dibHeader = new byte[40];
            BitConverter.GetBytes(40).CopyTo(dibHeader, 0); // DIB header size
            BitConverter.GetBytes(width).CopyTo(dibHeader, 4);
            BitConverter.GetBytes(height).CopyTo(dibHeader, 8);
            dibHeader[12] = 1; // Number of color planes
            dibHeader[14] = 32; // Bits per pixel
                                // Compression (0 = BI_RGB, no compression)
                                // Image size (can be 0 for BI_RGB)
            BitConverter.GetBytes(imageSize).CopyTo(dibHeader, 20);

            // Reverse the rows in the pixel data
            byte[] reversedData = new byte[imageSize];
            int rowSize = width * bytesPerPixel;
            for (int i = 0; i < height; i++)
            {
                Array.Copy(managedArray, i * rowSize, reversedData, (height - 1 - i) * rowSize, rowSize);
            }

            byte[] bmpData = null;
            using (var fs = new MemoryStream())
            {
                fs.Write(bmpFileHeader, 0, bmpFileHeader.Length);
                fs.Write(dibHeader, 0, dibHeader.Length);
                fs.Write(reversedData, 0, managedArray.Length);
                bmpData = fs.ToArray();
            }

            return bmpData;
        }
    }
}
