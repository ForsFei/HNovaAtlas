namespace IndustrialVision;

public static class CaptureChecks
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {label}");
    }
    private sealed class Fixture : IDisposable
    {
        public Guid Camera { get; } = Guid.NewGuid();
        public OfflineFrameSource Source { get; } = new([PlainPgm.LoadFixture()]);
        public SingleCaptureSession Session { get; }
        public CaptureRequest Request { get; private set; } = null!;
        public Fixture(double budget = 1)
        {
            Source.Open(); Session = new(Camera, Source.Session, 1, Source.Width, Source.Height, startupReviewed: true, frameBudget: budget);
            _ = Session.Update(Context(0), 0);
        }
        public CaptureContext Context(double time) => new(time, Camera, Source.Session, 1,
            AcquisitionMode.SoftwareSingle, true, true, true, true);
        public void Start(bool acknowledge = true)
        {
            Request = Session.Prepare("part-A", .01)!;
            _ = Session.TakeTrigger(.02);
            if (acknowledge) _ = Session.RecordDispatch(Request.Key, TriggerDispatchEvidence.Acknowledged, .03);
        }
        public TriggerAssociation Association(ulong sequence) => new(Request.Key, Camera, sequence, true);
        public CaptureFrame Packet(ulong? sequence, double receivedAt = .04)
        {
            using FrameLease lease = Source.Acquire();
            return new(lease.CopyToOwned(), Camera, 1, sequence, receivedAt, true, true);
        }
        public void Dispose() => Source.Dispose();
    }
    public static void Run()
    {
        count = 0;
        using (Fixture f = new())
        {
            SingleCaptureSession blocked = new(f.Camera, f.Source.Session, 1, 8, 6);
            _ = blocked.Update(f.Context(0), 0);
            Check(blocked.Prepare("part-A", .01) is null, "Default startup review does not authorize triggering");
            SingleCaptureSession noContext = new(f.Camera, f.Source.Session, 1, 8, 6, true);
            Check(noContext.Prepare("part-A", 0) is null, "No trigger request before verified context");
            f.Start(false);
            Check(f.Request.Key.OwnerSession == f.Session.OwnerSession && f.Request.ProfileRevision == 1, "Request freezes workpiece/profile with local owner identity");
            Check(f.Session.Prepare("part-B", .02) is null && f.Session.TakeTrigger(.02) is null, "Repeated clicks/polling cannot overwrite or take a second intent");
            Check(f.Session.Read(.02).Current?.Stage == CaptureStage.TriggerTaken, "Taking intent is not SDK acceptance or exposure");
            Check(!f.Session.RecordDispatch(f.Request.Key with { Number = 99 }, TriggerDispatchEvidence.Acknowledged, .03), "Foreign dispatch evidence ignored");
            Check(f.Session.RecordDispatch(f.Request.Key, TriggerDispatchEvidence.Acknowledged, .03)
                && f.Session.Read(.03).Current is { Stage: CaptureStage.AwaitingFrame, Result: null }, "Dispatch receipt does not supply frame identity or pixels");
            Check(f.Session.TakeForProcessing(.03) is null, "No processing offer before matched frame");
            Check(f.Session.Associate(f.Association(7), .035), "External qualified original-trigger association accepted");
            Check(f.Session.Associate(f.Association(7), .035), "Identical association is stable");
            _ = f.Session.Update(f.Context(.036) with { TriggerReady = false }, .036);
            CaptureFrame packet = f.Packet(7);
            Check(f.Session.ObserveFrame(packet, .04) == CaptureFrameDecision.Matched, "TriggerReady may go false while waiting; matching complete frame accepted");
            Check(f.Session.ObserveFrame(packet, .041) == CaptureFrameDecision.Duplicate, "Repeated same frame is not a second acquisition");
            CaptureFrame copiedPacket = packet with { Frame = new(packet.Frame.Stamp, Mono8Image.FromPacked(8, 6, packet.Frame.Image.CopyPixels())) };
            Check(f.Session.ObserveFrame(copiedPacket, .042) == CaptureFrameDecision.Duplicate, "Deep copied identical pixels retain duplicate identity");
            Check(!f.Session.ReleaseCapture(f.Request.Key, .043), "Cannot release before explicit processing ownership take");
            CaptureResult accepted = f.Session.TakeForProcessing(.05)!;
            Check(accepted.Request == f.Request && accepted.Evidence.Frame.Image.GetPixel(1, 1) == 220, "Workpiece identity travels with owned image into processing");
            Check(f.Session.TakeForProcessing(.05) is null, "Processing offer is taken once");
            Check(!f.Session.ReleaseCapture(f.Request.Key with { Number = 2 }, .05), "Foreign release cannot clear current capture");
            Check(f.Session.ReleaseCapture(f.Request.Key, .06) && f.Session.Read(.06).LastReleased == accepted, "Release retains last capture history, not inspection verdict");
            Check(f.Session.Prepare("part-B", .06) is null, "Still-busy trigger readiness prevents next request");
            _ = f.Session.Update(f.Context(.07), .07);
            CaptureRequest next = f.Session.Prepare("part-B", .08)!;
            Check(next.Key.Number == 2 && next.Key != f.Request.Key && f.Session.Read(.08).Current?.Stage == CaptureStage.Prepared, "Next request requires explicit preparation; no auto-trigger");
            _ = f.Session.TakeTrigger(.09); _ = f.Session.RecordDispatch(next.Key, TriggerDispatchEvidence.Acknowledged, .1);
            _ = f.Session.Associate(new(next.Key, f.Camera, 8, true), .11);
            Check(f.Session.ObserveFrame(packet, .12) == CaptureFrameDecision.Unrelated && f.Session.TakeForProcessing(.12) is null, "Previous request's delayed frame cannot become the next workpiece");
            Check(f.Session.ObserveFrame(f.Packet(8, .13), .13) == CaptureFrameDecision.Matched, "New original-trigger identity matches next frame");
        }
        using (Fixture f = new())
        {
            f.Start(); CaptureFrame packet = f.Packet(7);
            Check(f.Session.ObserveFrame(packet, .04) == CaptureFrameDecision.Uncorrelated, "Frame arrival order cannot invent missing trigger association");
            _ = f.Session.Associate(f.Association(7), .05);
            Check(f.Session.Read(.05).Current?.Result is null, "Association arrival does not silently drain/reassign previously uncorrelated pixels");
            Check(f.Session.ObserveFrame(packet, .06) == CaptureFrameDecision.Matched, "Caller may explicitly resubmit its still-owned fresh packet after genuine association");
        }
        using (Fixture f = new())
        {
            f.Start(false); _ = f.Session.Associate(f.Association(7), .03);
            Check(f.Session.ObserveFrame(f.Packet(7), .04) == CaptureFrameDecision.Matched
                && !f.Session.RecordDispatch(f.Request.Key, TriggerDispatchEvidence.Acknowledged, .05),
                "Qualified frame may precede dispatch receipt; late receipt cannot overwrite matched evidence");
        }
        using (Fixture f = new())
        {
            _ = f.Session.Prepare("part-A", .01);
            _ = f.Session.Update(f.Context(.02) with { TriggerReady = false }, .02);
            Check(f.Session.TakeTrigger(.03) is null && !f.Session.Read(.03).ReconciliationRequired,
                "Ready becoming false before take defers triggering without inventing a fault");
            _ = f.Session.Update(f.Context(.04), .04);
            Check(f.Session.TakeTrigger(.05) is not null, "New verified ready permits the same not-yet-taken intent once");
        }
        using (Fixture f = new())
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            Check(f.Session.ObserveFrame(f.Packet(null), .04) == CaptureFrameDecision.Uncorrelated, "No trigger identity means no match, even with frame number");
            Check(f.Session.ObserveFrame(f.Packet(8, .05), .05) == CaptureFrameDecision.Unrelated, "Different trigger ignored without freshening context");
        }
        foreach (string foreign in new[] { "camera", "source", "profile" })
        {
            using Fixture f = new(); f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            CaptureFrame packet = f.Packet(7);
            CaptureFrame bad = foreign switch
            {
                "camera" => packet with { CameraEpoch = Guid.NewGuid() },
                "source" => packet with { Frame = packet.Frame with { Stamp = packet.Frame.Stamp with { Session = Guid.NewGuid() } } },
                _ => packet with { ProfileRevision = 2 }
            };
            Check(f.Session.ObserveFrame(bad, .04) == CaptureFrameDecision.Unrelated
                && f.Session.Read(.04).Current?.Result is null, $"Old/foreign epoch cannot associate: {foreign}");
        }
        foreach (string invalid in new[] { "incomplete", "identity", "future", "nan", "before-trigger", "dimensions", "zero-frame" })
        {
            using Fixture f = new(); f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            CaptureFrame packet = f.Packet(7);
            CaptureFrame bad = invalid switch
            {
                "incomplete" => packet with { Complete = false },
                "identity" => packet with { IdentityQualified = false },
                "future" => packet with { ReceivedAt = 1 },
                "nan" => packet with { ReceivedAt = double.NaN },
                "before-trigger" => packet with { ReceivedAt = .01 },
                "dimensions" => packet with { Frame = packet.Frame with { Image = Mono8Image.FromPacked(1, 1, [0]) } },
                _ => packet with { Frame = packet.Frame with { Stamp = packet.Frame.Stamp with { Number = 0 } } }
            };
            Check(f.Session.ObserveFrame(bad, .04) == CaptureFrameDecision.Rejected
                && f.Session.Read(.04).ReconciliationRequired, $"Matching identity with bad evidence locks processing: {invalid}");
        }
        using (Fixture f = new())
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            CaptureFrame first = f.Packet(7); _ = f.Session.ObserveFrame(first, .04);
            Check(f.Session.ObserveFrame(f.Packet(7, .05), .05) == CaptureFrameDecision.Conflict
                && f.Session.Read(.05).Current?.Result?.Evidence == first, "Second frame for one trigger conflicts and retains original capture");
            Check(f.Session.TakeForProcessing(.05) is null, "Conflict cannot issue a processing offer");
        }
        using (Fixture f = new())
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            CaptureFrame first = f.Packet(7); _ = f.Session.ObserveFrame(first, .04);
            byte[] changed = first.Frame.Image.CopyPixels(); changed[0]++;
            CaptureFrame conflict = first with { Frame = new(first.Frame.Stamp, Mono8Image.FromPacked(8, 6, changed)) };
            Check(f.Session.ObserveFrame(conflict, .05) == CaptureFrameDecision.Conflict, "Same frame identity with changed pixels is not a harmless duplicate");
        }
        using (Fixture f = new(.1))
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            Check(f.Session.ObserveFrame(f.Packet(7, .2), .2) == CaptureFrameDecision.LateEvidenceOnly
                && f.Session.Read(.2).Current is { Stage: CaptureStage.Unknown, Result: null, LateFrame: not null }, "Late complete pixels record metadata only; deadline is not success");
            Check(f.Session.TakeForProcessing(.2) is null && f.Session.TakeTrigger(.2) is null, "Late frame cannot process or re-trigger automatically");
        }
        using (Fixture f = new())
        {
            f.Start(false); _ = f.Session.RecordDispatch(f.Request.Key, TriggerDispatchEvidence.Unknown, .03);
            Check(f.Session.Associate(f.Association(7), .035), "Qualified original association may be recorded after uncertain dispatch");
            Check(f.Session.ObserveFrame(f.Packet(7), .04) == CaptureFrameDecision.LateEvidenceOnly
                && f.Session.Read(.04).Current?.Stage == CaptureStage.Unknown, "Dispatch unknown stays unknown even if a correlated frame later arrives");
        }
        using (Fixture f = new())
        {
            f.Start(false); _ = f.Session.RecordDispatch(f.Request.Key, TriggerDispatchEvidence.NotAttempted, .03);
            Check(f.Session.Read(.03).Current?.Stage == CaptureStage.NotSubmitted, "Not attempted is a local boundary, not camera cancellation");
            Check(!f.Session.Associate(f.Association(7), .04), "Association cannot contradict explicit local not-attempted boundary");
        }
        using (Fixture f = new())
        {
            f.Start();
            Check(!f.Session.Associate(f.Association(7) with { Qualified = false }, .035), "Association qualification defaults cannot be skipped");
        }
        using (Fixture f = new())
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            Check(!f.Session.Associate(f.Association(8), .04), "Same request cannot change its original trigger association");
        }
        using (Fixture f = new())
        {
            f.Start();
            Check(!f.Session.Associate(f.Association(7) with { Key = new(Guid.NewGuid(), 1) }, .035), "Foreign owner request cannot install trigger association");
        }
        foreach (string loss in new[] { "quality", "grabbing", "permit", "camera", "source", "profile", "mode", "future", "repeat" })
        {
            using Fixture f = new(); f.Start(); CaptureContext good = f.Context(.04);
            CaptureContext bad = loss switch
            {
                "quality" => good with { Valid = false },
                "grabbing" => good with { Grabbing = false },
                "permit" => good with { ProcessPermit = false },
                "camera" => good with { CameraEpoch = Guid.NewGuid() },
                "source" => good with { SourceSession = Guid.NewGuid() },
                "profile" => good with { ProfileRevision = 2 },
                "mode" => good with { Mode = AcquisitionMode.HardwareSingle },
                "future" => good with { SampledAt = 1 },
                _ => good with { SampledAt = 0 }
            };
            _ = f.Session.Update(bad, .04);
            Check(f.Session.Read(.04).ReconciliationRequired && f.Session.TakeTrigger(.04) is null, $"Context loss is sticky: {loss}");
        }
        using (Fixture f = new())
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            Check(!f.Session.Update(f.Context(.3), .3) && f.Session.Read(.3).ReconciliationRequired, "Fresh context after an unobserved gap cannot unlock");
            Check(f.Session.ObserveFrame(f.Packet(7, .31), .31) == CaptureFrameDecision.LateEvidenceOnly, "Recovered input can retain original late metadata, never process it");
        }
        using (Fixture f = new())
        {
            _ = f.Session.Prepare("part-A", .01);
            Check(f.Session.TakeTrigger(.3) is null && f.Session.Read(.3).Current?.Stage == CaptureStage.NotSubmitted, "Freshness is rechecked before intent take");
        }
        using (Fixture f = new())
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035);
            CaptureFrame first = f.Packet(7); _ = f.Session.ObserveFrame(first, .04);
            _ = f.Session.TakeForProcessing(.05); _ = f.Session.ReleaseCapture(f.Request.Key, .06);
            CaptureRequest next = f.Session.Prepare("part-B", .07)!; _ = f.Session.TakeTrigger(.08);
            _ = f.Session.RecordDispatch(next.Key, TriggerDispatchEvidence.Acknowledged, .09);
            _ = f.Session.Associate(new(next.Key, f.Camera, 8, true), .1);
            Check(f.Session.ObserveFrame(first with { TriggerSequence = 8, ReceivedAt = .11 }, .11) == CaptureFrameDecision.Rejected, "Relabeled old owned-frame stamp cannot be reused for next trigger");
        }
        using (Fixture f = new())
        {
            f.Start(); Check(f.Session.Associate(f.Association(0), .035), "Normalized trigger zero can be valid; absence is nullable, not zero");
            _ = f.Session.ObserveFrame(f.Packet(0), .04); _ = f.Session.TakeForProcessing(.05); _ = f.Session.ReleaseCapture(f.Request.Key, .06);
            CaptureRequest next = f.Session.Prepare("part-B", .07)!; _ = f.Session.TakeTrigger(.08);
            Check(!f.Session.Associate(new(next.Key, f.Camera, 0, true), .09), "Normalized trigger identity cannot wrap/reuse in same camera epoch");
        }
        using (Fixture f = new())
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035); _ = f.Session.ObserveFrame(f.Packet(7), .04);
            f.Session.Invalidate();
            Check(f.Session.Read(.04).Current?.Result is not null && f.Session.TakeForProcessing(.04) is null, "Known historical match survives invalidation without new processing permission");
        }
        using (Fixture f = new())
        {
            f.Start(); _ = f.Session.Associate(f.Association(7), .035); _ = f.Session.ObserveFrame(f.Packet(7), .04);
            _ = f.Session.Update(f.Context(.2), .2); _ = f.Session.Update(f.Context(.3), .3);
            Check(f.Session.TakeForProcessing(.3) is null && f.Session.Read(.3).Current?.Result is not null,
                "Old matched image cannot be offered later even with continuously fresh camera context");
        }
        using (Fixture f = new())
        {
            f.Start(false);
            Check(!f.Session.RecordDispatch(f.Request.Key, (TriggerDispatchEvidence)99, .03)
                && f.Session.Read(.03).ReconciliationRequired, "Undefined dispatch evidence locks an unresolved trigger");
            Check(f.Session.Read(double.NaN).ReconciliationRequired, "Invalid clock cannot re-enable the session");
        }
        Reject<ArgumentException>(() => new SingleCaptureSession(Guid.Empty, Guid.NewGuid(), 1, 8, 6), "Missing reviewed camera epoch rejected");
        Reject<ArgumentException>(() => new SingleCaptureSession(Guid.NewGuid(), Guid.NewGuid(), 1, 8, 6, maxAge: 0), "Invalid age policy rejected");
        Console.WriteLine($"{count} offline capture-session checks passed; scripted associations only, no trigger/SDK/exposure/hardware.");
    }
    public static void Demo()
    {
        using Fixture f = new(.1); f.Start(); _ = f.Session.Associate(f.Association(7), .035);
        CaptureFrameDecision decision = f.Session.ObserveFrame(f.Packet(7, .2), .2);
        CaptureSessionView view = f.Session.Read(.2);
        Console.WriteLine($"Late frame: {decision}, request={view.Current?.Request.Workpiece}, stage={view.Current?.Stage}");
        Console.WriteLine($"Processing offered={f.Session.TakeForProcessing(.2) is not null}; locked={view.ReconciliationRequired}");
        Console.WriteLine("Pixels come from fixed P2 replay; trigger association manually declared, no physical trigger or camera.");
    }
}
