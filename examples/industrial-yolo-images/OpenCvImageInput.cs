using System.Runtime.InteropServices;
using System.Security.Cryptography;
using IndustrialYolo;
using IndustrialYolo.Onnx;
using OpenCvSharp;

namespace IndustrialYolo.Images;

public sealed record LoadedImage(string Path, string EncodedSha256, int EncodedBytes, int SourceChannels, Bgr24Image Bgr);
public sealed class PreparedImage
{
    public LoadedImage Source { get; }
    public ImageGeometry Geometry { get; }
    public YoloXInput Tensor { get; }
    internal PreparedImage(LoadedImage source, ImageGeometry geometry, YoloXInput tensor)
    { Source = source; Geometry = geometry; Tensor = tensor; }
}

public static class OpenCvImageInput
{
    public const int MaximumEncodedBytes = 16_777_216;

    public static LoadedImage Load(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Use a trusted local image, not a UNC path.");
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 3 or > MaximumEncodedBytes) throw new InvalidDataException("Image file must be3bytes..16MiB.");
        byte[] bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
        return Decode(bytes, path);
    }

    public static LoadedImage Decode(byte[] bytes, string sourceName = "owned-memory")
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 3 or > MaximumEncodedBytes) throw new InvalidDataException("Encoded image size invalid.");
        bool png = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        bool jpeg = bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        if (!png && !jpeg) throw new InvalidDataException("Only PNG/JPEG signatures are accepted, regardless of filename.");
        // Trusted local files only: this post-decode dimension check is NOT a decompression-bomb guard.
        using Mat decoded = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
        if (decoded.Empty()) throw new InvalidDataException("OpenCV could not decode the image.");
        ImageGeometry.ValidateDimensions(decoded.Cols, decoded.Rows);
        int channels = decoded.Channels();
        Bgr24Image owned;
        if (decoded.Type() == MatType.CV_8UC3) owned = CopyBgr(decoded);
        else if (decoded.Type() == MatType.CV_8UC1)
        {
            using Mat color = new();
            Cv2.CvtColor(decoded, color, ColorConversionCodes.GRAY2BGR);
            owned = CopyBgr(color);
        }
        else throw new InvalidDataException("Only8-bit gray/BGR images are supported. Explicitly convert alpha/16-bit images first.");
        return new(sourceName, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, channels, owned);
    }

    public static Bgr24Image CopyBgr(Mat source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Empty() || source.Dims != 2 || source.Type() != MatType.CV_8UC3)
            throw new ArgumentException("Need a nonempty2D CV_8UC3 Mat.");
        ImageGeometry.ValidateDimensions(source.Cols, source.Rows);
        int width = source.Cols, height = source.Rows;
        int stride = width * 3;
        byte[] bytes = new byte[stride * height];
        for (int y = 0; y < height; y++) Marshal.Copy(source.Ptr(y), bytes, y * stride, stride);
        return Bgr24Image.Create(source.Cols, source.Rows, stride, bytes);
    }

    public static PreparedImage Prepare(LoadedImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Bgr);
        ImageGeometry geometry = ImageGeometry.Create(source.Bgr.Width, source.Bgr.Height);
        using Mat original = ToMat(source.Bgr);
        using Mat resized = new();
        Cv2.Resize(original, resized, new Size(geometry.ResizedWidth, geometry.ResizedHeight), 0, 0, InterpolationFlags.Linear);
        using Mat canvas = new(416, 416, MatType.CV_8UC3, new Scalar(114, 114, 114));
        using (Mat destination = canvas.SubMat(new Rect(0, 0, geometry.ResizedWidth, geometry.ResizedHeight)))
            resized.CopyTo(destination);
        // Tensor owns its BGR float data; no Mat or ROI view escapes this method.
        YoloXInput input = YoloXInput.PrepareNoResize(CopyBgr(canvas));
        return new(source, geometry, input);
    }

    public static Mat ToMat(Bgr24Image source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Mat mat = new(source.Height, source.Width, MatType.CV_8UC3);
        try
        {
            byte[] row = new byte[source.Width * 3];
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                for (int channel = 0; channel < 3; channel++) row[x * 3 + channel] = source.Get(x, y, channel);
                Marshal.Copy(row, 0, mat.Ptr(y), row.Length);
            }
            return mat;
        }
        catch { mat.Dispose(); throw; }
    }
}
