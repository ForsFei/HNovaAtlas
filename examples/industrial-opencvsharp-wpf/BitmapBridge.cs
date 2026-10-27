using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialOpenCvSharp;
using IndustrialVision;
using OpenCvSharp;

namespace IndustrialOpenCvSharpWpf;

public static class BitmapBridge
{
    public static BitmapSource FromOwned(Mono8Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Freeze(image.Width, image.Height, image.CopyPixels());
    }
    public static BitmapSource FromMat(Mat gray)
    {
        // CopyMono8 checks 2D CV_8UC1 and copies each row, including noncontinuous ROI.
        byte[] packed = MatBasics.CopyMono8(gray);
        return Freeze(gray.Cols, gray.Rows, packed);
    }
    private static BitmapSource Freeze(int width, int height, byte[] packed)
    {
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, packed, width);
        bitmap.Freeze(); return bitmap;
    }
}

public sealed record RenderedMeasurement(BitmapSource Bitmap, StripeReport Report);
public static class NativeFrameRunner
{
    public static RenderedMeasurement Analyze(FrameSelection selection) => WithOwnedMat(selection.Image, image =>
        new RenderedMeasurement(BitmapBridge.FromMat(image), StripeDemo.Measure(image, selection.Recipe)));

    internal static T WithOwnedMat<T>(Mono8Image frame, Func<Mat, T> process)
    {
        using Mat image = new(frame.Height, frame.Width, MatType.CV_8UC1);
        byte[] packed = frame.CopyPixels();
        for (int row = 0; row < frame.Height; row++)
            Marshal.Copy(packed, row * frame.Width, image.Ptr(row), frame.Width);
        return process(image); // The callback borrows; Mat is disposed on success AND exception.
    }
}
