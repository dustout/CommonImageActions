using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CommonImageActions.AspNetCore
{
    /// <summary>
    /// Reads and writes the processed images that are kept on disk, and keeps the folder they are
    /// in below its size limit.
    /// </summary>
    internal static class DiskCache
    {
        private const string TempExtension = ".tmp";

        //how much each cache folder holds, so the folder does not have to be measured on every write
        private static readonly ConcurrentDictionary<string, FolderState> Folders = new ConcurrentDictionary<string, FolderState>(StringComparer.OrdinalIgnoreCase);

        private class FolderState
        {
            public long Bytes;
            public int IsTrimming;
            public volatile bool IsMeasured;
        }

        /// <summary>
        /// Read a cached file. Returns null when there is none, or it is older than the max age.
        /// </summary>
        public static async Task<byte[]> TryReadAsync(string filePath, TimeSpan? maxAge)
        {
            var file = new FileInfo(filePath);
            if (file.Exists == false)
            {
                return null;
            }

            if (maxAge.HasValue && DateTime.UtcNow - file.LastWriteTimeUtc > maxAge.Value)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(filePath);
        }

        /// <summary>
        /// Write a file to the cache, then delete the oldest files if the folder is over its limit.
        /// </summary>
        public static async Task WriteAsync(string filePath, byte[] data, long? maxFolderBytes, Action<Exception, string> logError)
        {
            var folder = Path.GetDirectoryName(filePath);
            if (Directory.Exists(folder) == false)
            {
                Directory.CreateDirectory(folder);
            }

            //write to a temporary name and rename it when it is complete, so that another request
            //can never read a file that is only half written
            var tempPath = $"{filePath}.{Guid.NewGuid():N}{TempExtension}";
            try
            {
                await File.WriteAllBytesAsync(tempPath, data);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                File.Move(tempPath, filePath);
            }
            finally
            {
                //if the rename failed, most likely because another request wrote the same file first
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            if (maxFolderBytes.HasValue == false || maxFolderBytes.Value <= 0)
            {
                return;
            }

            //the size of the folder is kept up to date here as files are written, so the folder
            //does not have to be measured on every write
            var state = Folders.GetOrAdd(folder, x => new FolderState());
            var total = Interlocked.Add(ref state.Bytes, data.Length);

            //the folder is measured properly the first time it is used (it may already hold files
            //from before the application started) and again whenever it looks to be over the limit
            if ((state.IsMeasured == false || total > maxFolderBytes.Value)
                && Interlocked.CompareExchange(ref state.IsTrimming, 1, 0) == 0)
            {
                //measuring a large folder and deleting files is not something a request should wait for
                _ = Task.Run(() =>
                {
                    try
                    {
                        Interlocked.Exchange(ref state.Bytes, Trim(folder, maxFolderBytes.Value));
                        state.IsMeasured = true;
                    }
                    catch (Exception ex)
                    {
                        logError(ex, $"Error trimming disk cache {folder}.");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref state.IsTrimming, 0);
                    }
                });
            }
        }

        /// <summary>
        /// Measure the folder and, if it is over its limit, delete the oldest files until it is
        /// comfortably under. Returns the size of what is left.
        /// </summary>
        private static long Trim(string folder, long maxFolderBytes)
        {
            if (Directory.Exists(folder) == false)
            {
                return 0;
            }

            var files = new DirectoryInfo(folder).EnumerateFiles()
                .OrderBy(x => x.LastWriteTimeUtc)
                .ToList();
            var total = files.Sum(x => x.Length);
            if (total <= maxFolderBytes)
            {
                return total;
            }

            //go a little under the limit so that the very next write does not trim again
            var target = (long)(maxFolderBytes * 0.8);
            foreach (var file in files)
            {
                if (total <= target)
                {
                    break;
                }

                try
                {
                    var length = file.Length;
                    file.Delete();
                    total -= length;
                }
                catch (IOException)
                {
                    //the file is being read right now, leave it for next time
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return total;
        }
    }
}
