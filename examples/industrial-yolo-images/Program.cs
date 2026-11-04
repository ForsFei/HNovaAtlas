using IndustrialYolo.Images;
using IndustrialYolo.Onnx;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

bool native = args.FirstOrDefault() is "--native-test" or "--infer" or "--demo" or "--compare";
try
{
    if (args is ["--self-test"]) { ImageChecks.Managed(); return 0; }
    if (args is ["--native-test", string model]) { ImageChecks.Native(model); return 0; }
    if (args is ["--infer", _, _] or ["--demo", _] or ["--compare", _, _, _])
    {
        string path = args[0] == "--demo" ? FixtureFiles.Chart(FixtureFiles.CreateDirectory()) : args[2];
        PreparedImage prepared = OpenCvImageInput.Prepare(OpenCvImageInput.Load(path));
        using YoloXNanoSession session = new(args[1]);
        YoloXRawOutput raw = session.Run(prepared.Tensor);
        PostprocessSettings settings = new(args[0] == "--infer" ? .1f : .01f, .45);
        ImageReport report = ImagePostprocessor.Process(raw, prepared, settings);
        Console.WriteLine($"OpenCV{Cv2.GetVersionString()} / CPU ORT{session.RuntimeVersion}. Local PNG/JPEG path; no accuracy or production-OK claim.");
        Console.WriteLine($"IMAGE={prepared.Source.Path}");
        Console.WriteLine(FormattableString.Invariant($"Source={prepared.Geometry.Width}x{prepared.Geometry.Height} r={prepared.Geometry.Ratio:G17} resized={prepared.Geometry.ResizedWidth}x{prepared.Geometry.ResizedHeight}, BGR0..255/top-left/pad114"));
        Console.WriteLine($"TENSOR_SHA256={prepared.Tensor.Sha256}; candidates={report.Candidates.Count} kept={report.Detections.Count} visible={report.Visible}");
        foreach (ImageDetection detection in report.Detections)
            Console.WriteLine(FormattableString.Invariant($"row={detection.Candidate.NetworkRow.RowIndex} classId={detection.Candidate.NetworkRow.ClassId} score={detection.Candidate.NetworkRow.Score:G9} source={detection.VisibleSource.Original} clipped={detection.VisibleSource.WasClipped}"));
        if (args[0] == "--compare") Console.WriteLine(ImageReference.Compare(prepared, raw, report, args[3]));
        return 0;
    }
    Console.Error.WriteLine("Usage: --self-test | --native-test <model> | --infer <model> <image> | --demo <model> | --compare <model> <image> <reference-prefix>");
    return 2;
}
catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or InvalidOperationException or NotSupportedException or
    OpenCVException or OnnxRuntimeException or DllNotFoundException or BadImageFormatException or TypeInitializationException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"FAILED, no fallback: {error.GetType().Name}: {error.Message}");
    return native ? 3 : 1;
}
