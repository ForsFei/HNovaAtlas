using System.Diagnostics;

namespace IndustrialOpenCvSharpWpf;

public sealed record ProcessResourceSample(PipelineSnapshot Pipeline, long ManagedBytes, long PrivateBytes, long WorkingSetBytes)
{
    public static ProcessResourceSample Read(MeasurementPipeline pipeline)
    {
        using Process process = Process.GetCurrentProcess(); process.Refresh();
        return new(pipeline.Snapshot, GC.GetTotalMemory(false), process.PrivateMemorySize64, process.WorkingSet64);
    }
}

public static class PipelineDemo
{
    public static async Task Run()
    {
        Guid producer = Guid.NewGuid(); FrameSelection bright = FrameFactory.Create(0);
        MeasurementPipeline pipeline = new(producer, 128, 96, bright.Recipe, "stripe-v1", 2);
        LatestMeasurementPreview preview = new();
        MeasurementJob a = new(producer, 1, Guid.NewGuid(), bright);
        MeasurementJob b = new(producer, 2, Guid.NewGuid(), FrameFactory.Create(2));
        MeasurementJob c = new(producer, 3, Guid.NewGuid(), FrameFactory.Create(3));
        try
        {
            Console.WriteLine($"Before: {ProcessResourceSample.Read(pipeline)}");
            preview.Publish(a); PipelineSubmission first = pipeline.TrySubmit(a);
            preview.Publish(b); PipelineSubmission second = pipeline.TrySubmit(b);
            preview.Publish(c); PipelineSubmission refused = pipeline.TrySubmit(c);
            Console.WriteLine($"Admission: A={first.Admission}, B={second.Admission}, C={refused.Admission}; preview={preview.Snapshot}");
            if (first.Completion is null || second.Completion is null || refused.Admission != PipelineAdmission.Full) throw new InvalidOperationException("Demo admission mismatch.");
            MeasurementJob shown = preview.Take()!;
            foreach (MeasurementTerminal result in await Task.WhenAll(first.Completion, second.Completion))
            {
                Console.WriteLine($"#{result.Job.Number}: {result.Stage}/{result.Output?.Report.Outcome}; matches current preview={result.MatchesDisplay(shown, pipeline.Session)}");
                if (result.Stage != PipelineStage.Completed) throw new InvalidOperationException($"Actual backend failed: {result.Error}");
                // Local demo consumes by logging; NOT durable storage or a PLC acknowledgement.
                if (!pipeline.Acknowledge(result)) throw new InvalidOperationException("Receipt acknowledgement failed.");
            }
            Console.WriteLine("Explicit offline retry of previously unaccepted C; not automatic reshoot/replay or freshness approval.");
            PipelineSubmission retry = pipeline.TrySubmit(c);
            if (retry.Completion is null) throw new InvalidOperationException("Retry not admitted.");
            MeasurementTerminal final = await retry.Completion;
            Console.WriteLine($"#{final.Job.Number}: {final.Stage}/{final.Output?.Report.Outcome}; matches preview={final.MatchesDisplay(shown, pipeline.Session)}; width={final.Output?.Report.WidthPixels?.ToString("F3") ?? "absent"}");
            if (final.Stage != PipelineStage.Completed || !pipeline.Acknowledge(final)) throw new InvalidOperationException("Final actual processing failed.");
        }
        finally { pipeline.Complete(); preview.Close(); await pipeline.Completion; }
        Console.WriteLine($"After: {ProcessResourceSample.Read(pipeline)}");
        Console.WriteLine("Memory samples are not leak/throughput acceptance; no forced GC, camera, disk persistence, PLC or product verdict.");
    }
}
