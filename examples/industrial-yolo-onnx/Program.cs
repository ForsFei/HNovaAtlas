using IndustrialYolo.Onnx;
using Microsoft.ML.OnnxRuntime;

bool native = args.FirstOrDefault() is "--native-test" or "--demo" or "--compare-reference" or
    "--postprocess-native-test" or "--postprocess-demo" or "--compare-postprocess";
try
{
    if (args is ["--fetch-model", string destination])
    {
        await ModelArtifact.FetchAsync(destination);
        Console.WriteLine($"Pinned YOLOX-Nano verified: {Path.GetFullPath(destination)}\nSHA256={ModelArtifact.Sha256}");
        return 0;
    }
    if (args is ["--self-test"]) { Checks.Managed(); return 0; }
    if (args is ["--native-test", string model]) { Checks.Native(model); return 0; }
    if (args is ["--postprocess-test"]) { PostprocessChecks.Managed(); return 0; }
    if (args is ["--postprocess-native-test", string postprocessModel]) { PostprocessChecks.Native(postprocessModel); return 0; }
    if (args is ["--postprocess-demo", _] or ["--compare-postprocess", _, _])
    {
        using YoloXNanoSession session = new(args[1]);
        YoloXInput input = YoloXInput.PrepareNoResize(YoloXInput.CreateFixture());
        YoloXRawOutput output = session.Run(input);
        PostprocessReport report = YoloXPostprocessor.Process(output, input, new(.01f, .45));
        Console.WriteLine($"Actual YOLOX-Nano CPU output decoded. Synthetic gradient, threshold0.01 for NUMERICAL illustration, NOT real objects/quality.");
        Console.WriteLine($"Rows={report.Rows.Count} Filtered={report.Filtered} Candidates={report.Candidates.Count} Suppressed={report.Suppressions.Count} Kept={report.Detections.Count} Visible={report.Visible}; class-agnostic +1 NMS, r=1 source.");
        foreach (YoloXDetection detection in report.Detections)
            Console.WriteLine(FormattableString.Invariant($"row={detection.Candidate.RowIndex} classId={detection.Candidate.ClassId} score={detection.Candidate.Score:G9} source={detection.Source.Original} clipped={detection.Source.WasClipped}"));
        if (args[0] == "--compare-postprocess")
        {
            PostprocessComparison comparison = PostprocessReference.CompareFile(report, input.Sha256, args[2]);
            Console.WriteLine(FormattableString.Invariant($"NumPy postprocess comparison: rows={comparison.Rows}, candidates={comparison.Candidates}, kept={comparison.Detections}, maxCoordinateError={comparison.MaximumCoordinateError:G9}. No independent quality benchmark."));
        }
        return 0;
    }
    if (args is ["--demo", _] or ["--compare-reference", _, _])
    {
        using YoloXNanoSession session = new(args[1]);
        YoloXInput input = YoloXInput.PrepareNoResize(YoloXInput.CreateFixture());
        YoloXRawOutput result = session.Run(input);
        Console.WriteLine($"Actual CPU ONNX Runtime {session.RuntimeVersion}; pinned YOLOX-Nano. Synthetic image, NOT object-detection quality.");
        Console.WriteLine($"Input images [{string.Join(',', input.Shape)}] BGR float32 0..255; SHA256={input.Sha256}");
        Console.WriteLine($"Output output [{string.Join(',', result.Shape)}], {result.Values.Length} raw floats; no decode/NMS.");
        NumericReference.CheckSamples(result);
        Console.WriteLine("20 Python reference samples passed: abs<=1e-5 + 1e-4*abs(reference).");
        if (args[0] == "--compare-reference")
        {
            NumericComparison comparison = NumericReference.CompareFile(result, args[2]);
            Console.WriteLine(FormattableString.Invariant($"Full Python raw comparison: {comparison.Count} values; maxAbsError={comparison.MaximumAbsoluteError:G9}. Same ORT backend, not an independent accuracy benchmark."));
        }
        return 0;
    }
    Console.Error.WriteLine("Usage: --self-test | --fetch-model <destination.onnx> | --native-test <model.onnx> | --demo <model.onnx> | --compare-reference <model.onnx> <reference.f32> | --postprocess-test | --postprocess-native-test <model> | --postprocess-demo <model> | --compare-postprocess <model> <reference.json>");
    return 2;
}
catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or
    IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or
    OnnxRuntimeException or DllNotFoundException or BadImageFormatException or TypeInitializationException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"FAILED, no fallback: {error.GetType().Name}: {error.Message}");
    return native ? 3 : 1;
}
