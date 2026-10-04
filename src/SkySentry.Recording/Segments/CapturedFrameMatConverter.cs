using OpenCvSharp;
using SkySentry.Core.Frames;

namespace SkySentry.Recording.Segments;

/// <summary>
/// Converts a <see cref="CapturedFrame"/> (owned BGR24 byte buffer) into an
/// OpenCvSharp <see cref="Mat"/>, respecting stride/padding. Shared by
/// <see cref="SegmentedCameraRecorder"/>. Extracted from Phase 1's
/// FileSystemEventRecorder, which this project's Phase 2 code replaces.
/// </summary>
internal static class CapturedFrameMatConverter
{
    public static Mat ToMat(CapturedFrame frame)
    {
        var mat = new Mat(frame.Height, frame.Width, MatType.CV_8UC3);
        var rowBytes = frame.Width * 3;
        var destStep = (int)mat.Step();

        if (frame.Stride == rowBytes && destStep == rowBytes)
        {
            System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, 0, mat.Data, frame.Pixels.Length);
        }
        else
        {
            // Source/destination row strides differ (padding) — copy row by row so
            // we never read/write past either buffer's actual row length.
            for (var row = 0; row < frame.Height; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    frame.Pixels, row * frame.Stride, mat.Data + row * destStep, rowBytes);
            }
        }

        return mat;
    }
}
