
using CommonImageActions.Core;
using Microsoft.Maui.Graphics.Skia;
using PDFiumCore;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CommonImageActions.Pdf
{
    public static class PdfProcessor
    {
        private static bool isPdfiumInitalized = false;

        public static async Task<IEnumerable<byte[]>> ProcessPdfsAsync(IEnumerable<byte[]> pdfsData, ImageActions actions)
        {
            var returnList = new List<byte[]>();

            var activeJobs = new List<Task<byte[]>>();
            foreach (var pdfData in pdfsData)
            {
                var job = ProcessPdfAsync(pdfData, actions);
                activeJobs.Add(job);
            }

            await Task.WhenAll(activeJobs);

            //by going through it this way we keep the order the same as it came in
            foreach (var completeJob in activeJobs)
            {
                returnList.Add(completeJob.Result);
            }

            return returnList;
        }

        public async static Task<byte[]> ProcessPdfAsync(byte[] imageData, ImageActions actions)
        {
            return await ProcessHelperAsync(imageData, actions);
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
                await imageStream.CopyToAsync(ms);
                imageData = ms.ToArray();
            }

            return await ProcessHelperAsync(imageData, actions);
        }

        private static async Task<byte[]> ProcessHelperAsync(byte[] pdfData, ImageActions actions)
        {
            byte[] returnValue = null;

            //make sure pdfium is initalized
            if (isPdfiumInitalized == false)
            {
                fpdfview.FPDF_InitLibrary();
                isPdfiumInitalized = true;
            }

            await Task.Run(() =>
            {
                var handle = GCHandle.Alloc(pdfData, GCHandleType.Pinned);
                try
                {
                    var pdfDocument = fpdfview.FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), pdfData.Length, actions.PdfPassword);
                    if (pdfDocument == null)
                    {
                        throw new NotImplementedException("Error loading pdf");
                    }

                    try
                    {
                        //make sure there is at least one page
                        var pageCount = fpdfview.FPDF_GetPageCount(pdfDocument);
                        if (pageCount == 0)
                        {
                            throw new NotImplementedException("Error loading pdf");
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
                            throw new NotImplementedException("Error loading pdf");
                        }

                        try
                        {
                            //get dimensions of the page
                            var pdfWidth = (int)fpdfview.FPDF_GetPageWidth(pdfPage);
                            var pdfHeight = (int)fpdfview.FPDF_GetPageHeight(pdfPage);

                            //create a bitmap of the page
                            var pdfBitmap = fpdfview.FPDFBitmapCreate(pdfWidth, pdfHeight, 0);
                            fpdfview.FPDFBitmapFillRect(pdfBitmap, 0, 0, pdfWidth, pdfHeight, 0xFFFFFFFF);
                            fpdfview.FPDF_RenderPageBitmap(pdfBitmap, pdfPage, 0, 0, pdfWidth, pdfHeight, 0, 0);

                            try
                            {
                                //get handle to buffer
                                var buffer = fpdfview.FPDFBitmapGetBuffer(pdfBitmap);
                                var stride = fpdfview.FPDFBitmapGetStride(pdfBitmap);
                                var bufferSize = stride * pdfHeight;

                                // Copy data from unmanaged buffer to managed array
                                byte[] managedArray = new byte[bufferSize];
                                Marshal.Copy(buffer, managedArray, 0, bufferSize);

                                //convert from BGRA32 format to BMP format
                                var bmpData = ConvertFromBGRA32ToBmp(managedArray, pdfWidth, pdfHeight);

                                //convert into skia format
                                using var originalBitmap = SKBitmap.Decode(bmpData);
                                using var newImage = new SkiaImage(originalBitmap);

                                //process skia image into encoded image
                                returnValue = ImageProcessor.EncodeSkiaImage(newImage, actions).ToArray();
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
                    handle.Free();
                }
            });

            return returnValue;
        }

        private static byte[] ConvertFromBGRA32ToBmp(byte[] managedArray, int width, int height)
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
