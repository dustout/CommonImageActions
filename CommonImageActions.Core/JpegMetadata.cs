using System;
using System.Collections.Generic;

namespace CommonImageActions.Core
{
    /// <summary>
    /// Copies the metadata (exif, xmp, iptc) of one jpeg into another. Skia can not write metadata
    /// itself, so the segments that hold it are lifted out of the original file and placed into
    /// the result as they are.
    /// </summary>
    internal static class JpegMetadata
    {
        private const byte MarkerPrefix = 0xFF;
        private const byte StartOfImage = 0xD8;
        private const byte EndOfImage = 0xD9;
        private const byte StartOfScan = 0xDA;
        private const byte App0 = 0xE0;
        private const byte App1 = 0xE1; //exif and xmp
        private const byte App13 = 0xED; //iptc

        private const int ExifOrientationTag = 0x0112;

        /// <summary>
        /// Returns the output with the metadata of the source added. When either is not a jpeg,
        /// or the source can not be read, the output is returned unchanged.
        /// </summary>
        public static byte[] CopyMetadata(byte[] source, byte[] output)
        {
            try
            {
                if (IsJpeg(source) == false || IsJpeg(output) == false)
                {
                    return output;
                }

                var segments = ReadMetadataSegments(source);
                if (segments.Count == 0)
                {
                    return output;
                }

                //metadata goes straight after the start of image marker, or after the jfif header if there is one
                var insertAt = 2;
                if (output.Length >= 6 && output[2] == MarkerPrefix && output[3] == App0)
                {
                    var jfifLength = (output[4] << 8) | output[5];
                    if (4 + jfifLength <= output.Length)
                    {
                        insertAt = 4 + jfifLength;
                    }
                }

                var metadataLength = 0;
                foreach (var segment in segments)
                {
                    metadataLength += segment.Length;
                }

                var result = new byte[output.Length + metadataLength];
                Buffer.BlockCopy(output, 0, result, 0, insertAt);

                var position = insertAt;
                foreach (var segment in segments)
                {
                    Buffer.BlockCopy(segment, 0, result, position, segment.Length);
                    position += segment.Length;
                }

                Buffer.BlockCopy(output, insertAt, result, position, output.Length - insertAt);
                return result;
            }
            catch (Exception)
            {
                //metadata is a nice to have, a file that can not be read should not stop the image being returned
                return output;
            }
        }

        private static bool IsJpeg(byte[] data)
        {
            return data != null && data.Length > 4 && data[0] == MarkerPrefix && data[1] == StartOfImage;
        }

        private static List<byte[]> ReadMetadataSegments(byte[] source)
        {
            var segments = new List<byte[]>();

            //a jpeg is a list of segments, each one is a marker (FF xx) then a two byte length then its data
            var offset = 2;
            while (offset + 4 <= source.Length)
            {
                if (source[offset] != MarkerPrefix)
                {
                    break;
                }

                var marker = source[offset + 1];

                //markers can be padded with extra FF bytes
                if (marker == MarkerPrefix)
                {
                    offset++;
                    continue;
                }

                //the image data starts here, all of the headers have been read
                if (marker == StartOfScan || marker == EndOfImage)
                {
                    break;
                }

                var length = (source[offset + 2] << 8) | source[offset + 3];
                if (length < 2 || offset + 2 + length > source.Length)
                {
                    break;
                }

                if (marker == App1 || marker == App13)
                {
                    var segment = new byte[2 + length];
                    Buffer.BlockCopy(source, offset, segment, 0, segment.Length);

                    if (marker == App1 && IsExif(segment))
                    {
                        ResetOrientation(segment);
                    }

                    segments.Add(segment);
                }

                offset += 2 + length;
            }

            return segments;
        }

        private static bool IsExif(byte[] segment)
        {
            //marker (2) + length (2) + "Exif\0\0"
            return segment.Length >= 10
                && segment[4] == 'E' && segment[5] == 'x' && segment[6] == 'i' && segment[7] == 'f'
                && segment[8] == 0 && segment[9] == 0;
        }

        /// <summary>
        /// The pixels of the result have already been turned the right way up, so the orientation
        /// that came from the original has to be set back to normal or it would be applied twice.
        /// The embedded thumbnail is dropped as well since it still shows the original.
        /// </summary>
        private static void ResetOrientation(byte[] segment)
        {
            //exif data is a small tiff file that starts after the "Exif\0\0" identifier
            var tiff = 10;
            if (segment.Length < tiff + 8)
            {
                return;
            }

            bool isLittleEndian;
            if (segment[tiff] == 'I' && segment[tiff + 1] == 'I')
            {
                isLittleEndian = true;
            }
            else if (segment[tiff] == 'M' && segment[tiff + 1] == 'M')
            {
                isLittleEndian = false;
            }
            else
            {
                return;
            }

            //the first directory of tags
            var directory = tiff + (long)ReadUInt32(segment, tiff + 4, isLittleEndian);
            if (directory + 2 > segment.Length)
            {
                return;
            }

            var entryCount = ReadUInt16(segment, (int)directory, isLittleEndian);
            for (var i = 0; i < entryCount; i++)
            {
                //each entry is tag (2) type (2) count (4) value (4)
                var entry = directory + 2 + (12L * i);
                if (entry + 12 > segment.Length)
                {
                    return;
                }

                if (ReadUInt16(segment, (int)entry, isLittleEndian) == ExifOrientationTag)
                {
                    WriteUInt16(segment, (int)entry + 8, 1, isLittleEndian);
                }
            }

            //after the entries is the position of the next directory, which holds the thumbnail
            var nextDirectory = directory + 2 + (12L * entryCount);
            if (nextDirectory + 4 <= segment.Length)
            {
                for (var i = 0; i < 4; i++)
                {
                    segment[nextDirectory + i] = 0;
                }
            }
        }

        private static int ReadUInt16(byte[] data, int offset, bool isLittleEndian)
        {
            return isLittleEndian
                ? data[offset] | (data[offset + 1] << 8)
                : (data[offset] << 8) | data[offset + 1];
        }

        private static uint ReadUInt32(byte[] data, int offset, bool isLittleEndian)
        {
            return isLittleEndian
                ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
                : (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static void WriteUInt16(byte[] data, int offset, int value, bool isLittleEndian)
        {
            if (isLittleEndian)
            {
                data[offset] = (byte)(value & 0xFF);
                data[offset + 1] = (byte)((value >> 8) & 0xFF);
            }
            else
            {
                data[offset] = (byte)((value >> 8) & 0xFF);
                data[offset + 1] = (byte)(value & 0xFF);
            }
        }
    }
}
