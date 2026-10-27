using System.Runtime.InteropServices;
using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public static class MatBasics
{
    // The caller must keep mat alive and unmodified during this synchronous copy.
    public static byte[] CopyMono8(Mat mat)
    {
        ArgumentNullException.ThrowIfNull(mat);
        if (mat.Empty() || mat.Dims != 2 || mat.Type() != MatType.CV_8UC1) throw new ArgumentException("Nonempty 2D CV_8UC1 required; no implicit conversion.");
        long stride = mat.Step();
        int rows = mat.Rows, columns = mat.Cols;
        // This computed envelope is not proof of arbitrary external buffer validity.
        int size = PixelLayout.Validate(columns, rows, stride, (rows - 1L) * stride + columns);
        byte[] packed = new byte[size];
        for (int row = 0; row < rows; row++) Marshal.Copy(mat.Ptr(row), packed, row * columns, columns);
        return packed;
    }
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        Console.WriteLine($"Actual OpenCV runtime: {Cv2.GetVersionString()}");
        using Mat empty = new(); Check(empty.Empty(), "Default Mat is empty, not a black image");
        Reject(() => CopyMono8(empty), "Empty input refused");
        string file = Path.Combine(AppContext.BaseDirectory, "fixtures", "contrast-target.pgm");
        using Mat image = Cv2.ImRead(file, ImreadModes.Grayscale);
        Check(!image.Empty() && image.Rows == 6 && image.Cols == 8, "Actual P2 decode yields rows6/cols8");
        Check(image.Type() == MatType.CV_8UC1 && image.Channels() == 1, "Decoded type is explicitly verified");
        Check(image.ElemSize() == 1 && image.Total() == 48, "Total counts pixels, ElemSize counts bytes per pixel");
        Check(image.At<byte>(1, 1) == 220 && image.At<byte>(4, 7) == 255, "Row/column accessor reads original fixture values");
        Check(CopyMono8(image).Length == 48 && CopyMono8(image)[4 * 8] == 0, "Row-wise copy preserves actual pixel layout");
        using Mat roi = new(image, new Rect(1, 1, 2, 2));
        Check(roi.Rows == 2 && roi.Cols == 2 && roi.Step() == image.Step(), "ROI keeps parent row stride");
        Check(!roi.IsContinuous() && roi.IsSubmatrix(), "Narrow two-row ROI is actually noncontinuous");
        Check(CopyMono8(roi).SequenceEqual(new byte[] { 220, 220, 220, 220 }), "Noncontinuous ROI copies without crossing parent columns");
        using Mat deep = roi.Clone();
        Check(deep.IsContinuous() && !deep.IsSubmatrix(), "Clone is independent continuous pixel storage");
        roi.Set(0, 0, (byte)42);
        Check(image.At<byte>(1, 1) == 42 && deep.At<byte>(0, 0) == 220, "ROI shares parent writes; Clone does not");
        Mat alias = image; Check(ReferenceEquals(alias, image), "C# assignment aliases same wrapper, not a new header");
        using Mat destination = new(); deep.CopyTo(destination);
        deep.Set(0, 0, (byte)17); Check(destination.At<byte>(0, 0) == 220, "CopyTo empty destination creates detached output here");
        using Mat survivor = SurvivingRoi(); Check(survivor.At<byte>(0, 0) == 9, "OpenCV-owned ROI data survives parent header disposal");
        using Mat color = new(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        Vec3b bgr = color.At<Vec3b>(0, 0);
        Check(bgr.Item0 == 10 && bgr.Item1 == 20 && bgr.Item2 == 30, "Three-byte pixel stores B/G/R convention used here");
        Check(color.Total() == 1 && color.ElemSize() == 3 && color.Channels() == 3, "Three channels are not three pixels");
        Reject(() => CopyMono8(color), "Mono8 copy refuses BGR rather than silently reinterpreting it");
        using Mat gray = new(); Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        Check(gray.Type() == MatType.CV_8UC1 && gray.At<byte>(0, 0) == 22, "Actual BGR-to-gray conversion is explicit");
        using Mat high = new(1, 1, MatType.CV_16UC1, new Scalar(1000));
        Check(high.At<ushort>(0, 0) == 1000 && high.ElemSize() == 2, "16-bit grayscale is not an 8-bit byte view");
        Reject(() => CopyMono8(high), "Mono8 copy refuses 16-bit input");
        using Mat saturated = new(); high.ConvertTo(saturated, MatType.CV_8UC1);
        Check(saturated.At<byte>(0, 0) == 255, "Unscaled 16-to-8 conversion saturates instead of auto-normalizing");
        using Mat scaled = new(); high.ConvertTo(scaled, MatType.CV_8UC1, .1);
        Check(scaled.At<byte>(0, 0) == 100, "Explicit alpha controls numeric scaling");
        Check(Cv2.ImEncode(".png", destination, out byte[] png) && png.Length > 0, "PNG encoder produces owned memory bytes, not a raw pixel array");
        using Mat decoded = Cv2.ImDecode(png, ImreadModes.Grayscale);
        Check(CopyMono8(decoded).SequenceEqual(CopyMono8(destination)), "Actual lossless PNG memory roundtrip matches pixels");
        Reject(() => { PixelLayout.ValidateRoi(8, 6, 7, 5, 2, 2); }, "ROI validated before creating native view");
        Console.WriteLine($"Native Mat checks passed: {count}. Actual OpenCV allocation/decode/ROI/conversion/encode only; no camera, WPF, detection accuracy or long-term memory validation.");
    }
    private static Mat SurvivingRoi()
    {
        using Mat parent = new(3, 4, MatType.CV_8UC1, new Scalar(9));
        return new Mat(parent, new Rect(1, 1, 2, 2)); // New header retains OpenCV-owned storage; not external borrowed memory.
    }
}
