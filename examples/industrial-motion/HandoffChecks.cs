namespace IndustrialMotion;

internal static class HandoffChecks
{
    private static int _count;
    private static readonly Guid Plc = Guid.NewGuid();
    private static PlcHandoffInput Input(double time) => new(time, Plc, Valid: true, RemoteAuto: true, ProcessPermit: true);
    private static StationMotionJob Job(ushort id = 1) => new(new(Plc, id), "part-A", 1, 1);
    private static PlcMotionHandoff New()
    {
        var bridge = new PlcMotionHandoff(Plc, 1, new ushort[] { 1, 2 }); bridge.Update(Input(0), 0); return bridge;
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static MotionExecutionIntent Take(PlcMotionHandoff bridge)
    {
        if (bridge.Receive(Job(), .01) != HandoffReply.Accepted) throw new InvalidOperationException("Fixture receive failed.");
        return bridge.TakeIntent(.02) ?? throw new InvalidOperationException("Fixture take failed.");
    }
    private static void Finish(PlcMotionHandoff bridge, MotionExecutionIntent intent)
    {
        bridge.RecordDispatch(intent.Ticket, DispatchEvidence.Acknowledged, .03);
        bridge.Observe(new(intent.Ticket, .04, MotionReportKind.Completed, true), .04);
    }
    public static void Demo()
    {
        var bridge = New(); var intent = Take(bridge);
        Console.WriteLine($"PLC request accepted, local intent={intent.Ticket.Attempt}; no SDK call made");
        Console.WriteLine($"Repeated request={bridge.Receive(Job(), .021)}, second intent exists={bridge.TakeIntent(.022) is not null}");
        Finish(bridge, intent);
        Console.WriteLine($"Qualified fixture outcome={bridge.Read(.05).Retained?.Stage}, intents taken={bridge.IntentsTaken}");
        Console.WriteLine($"Matching acknowledgement={bridge.AcknowledgeResult(Job().Key, bridge.ExecutorSession, .06)}, old id afterward={bridge.Receive(Job(), .07)}");
        var unknown = New(); var u = Take(unknown); unknown.RecordDispatch(u.Ticket, DispatchEvidence.OutcomeUnknown, .03);
        unknown.Update(Input(.1), .1);
        Console.WriteLine($"Restored input: reconciliation={unknown.Read(.11).ReconciliationRequired}; retained={unknown.Read(.11).Retained?.Stage}; no stop/replay executed");
    }
    public static void Run()
    {
        var bridge = new PlcMotionHandoff(Plc, 1, new ushort[] { 1 });
        Check(bridge.Receive(Job(), 0) == HandoffReply.Blocked && bridge.TakeIntent(0) == null, "No intent before verified input");
        bridge = New(); Check(bridge.Receive(Job(), .01) == HandoffReply.Accepted, "Valid high-level preset request accepted");
        Check(bridge.Read(.01).Retained?.Stage == HandoffStage.Accepted && bridge.IntentsTaken == 0, "Accepted is not a native call or movement");
        Check(bridge.Receive(Job(), .011) == HandoffReply.Duplicate, "Held request level deduplicates exact identity and payload");
        Check(bridge.Receive(Job() with { PositionId = 2 }, .012) == HandoffReply.Conflict, "Same identity changed target conflicts");
        Check(bridge.Receive(Job(2), .013) == HandoffReply.Busy, "New request cannot overwrite retained request");
        var intent = bridge.TakeIntent(.02)!;
        Check(intent.Job == Job() && bridge.Read(.02).Retained?.Stage == HandoffStage.DispatchPending && bridge.IntentsTaken == 1, "One intent take records attempted-boundary state only");
        Check(bridge.TakeIntent(.021) == null && bridge.Receive(Job(), .022) == HandoffReply.Duplicate, "Repeated polling cannot take another intent");
        Check(!bridge.AcknowledgeResult(Job().Key, bridge.ExecutorSession, .023), "Acknowledgement cannot release active work");
        Check(!bridge.Observe(new(intent.Ticket, .024, MotionReportKind.Completed, true), .024), "Completion report before dispatch evidence refused");
        Check(bridge.RecordDispatch(intent.Ticket, DispatchEvidence.Acknowledged, .03), "Native-call acknowledgement recorded separately");
        Check(bridge.Read(.03).Retained?.Stage == HandoffStage.CallAcknowledged, "Zero-like call acknowledgement is not motion done");
        Check(!bridge.RecordDispatch(intent.Ticket, DispatchEvidence.Acknowledged, .031), "No second dispatch registration");
        Check(bridge.Observe(new(intent.Ticket, .04, MotionReportKind.Running, true), .04), "Qualified normalized running report accepted");
        Check(bridge.Read(.04).Retained?.Stage == HandoffStage.Running, "Running remains nonterminal");
        bridge.Observe(new(intent.Ticket, .05, MotionReportKind.Completed, true), .05);
        Check(bridge.Read(.05).Retained?.Stage == HandoffStage.Completed && bridge.IntentsTaken == 1, "Only qualified matching completion becomes terminal");
        Check(bridge.Receive(Job(2), .051) == HandoffReply.Busy, "Completed result retained until matching acknowledgement");
        Check(!bridge.AcknowledgeResult(Job(2).Key, bridge.ExecutorSession, .052), "Wrong PLC request acknowledgement refused");
        Check(!bridge.AcknowledgeResult(Job().Key, Guid.NewGuid(), .053), "Wrong executor session acknowledgement refused");
        Check(bridge.AcknowledgeResult(Job().Key, bridge.ExecutorSession, .06), "Matching terminal acknowledgement releases record only");
        Check(bridge.Read(.06).Retained == null && bridge.Read(.06).HighWater == 1, "High-water survives result release");
        Check(bridge.Receive(Job(), .07) == HandoffReply.Stale, "Released old request cannot re-execute");
        Check(bridge.Receive(Job(2), .08) == HandoffReply.Accepted, "Explicit new id accepted only after prior result release");
        foreach (var bad in new[] { Job() with { RecipeRevision = 2 }, Job() with { PositionId = 9 }, Job() with { Workpiece = " " } })
        {
            bridge = New(); Check(bridge.Receive(bad, .01) == HandoffReply.Rejected, "Unsupported recipe/preset/workpiece retained as rejection");
            Check(bridge.Read(.02).Retained?.Stage == HandoffStage.Rejected && bridge.TakeIntent(.02) == null, "Rejected request issues no execution intent");
        }
        bridge = New(); Check(bridge.Receive(Job(0), .01) == HandoffReply.InvalidIdentity, "Zero PLC request id refused");
        Check(bridge.Receive(Job() with { Key = new(Guid.NewGuid(), 1) }, .02) == HandoffReply.InvalidIdentity, "Foreign request key cannot switch verified PLC boot");
        bridge = New(); intent = Take(bridge);
        Check(bridge.RecordDispatch(intent.Ticket, DispatchEvidence.NotAttempted, .03) && bridge.Read(.03).Retained?.Stage == HandoffStage.NotSubmitted,
            "Explicit not-attempted evidence distinct from unknown");
        bridge = New(); intent = Take(bridge); bridge.RecordDispatch(intent.Ticket, DispatchEvidence.OutcomeUnknown, .03);
        Check(bridge.Read(.03).ReconciliationRequired && bridge.Read(.03).Retained?.Stage == HandoffStage.Unknown, "Ambiguous native dispatch locks further work without claiming stop");
        bridge.Update(Input(.1), .1);
        Check(bridge.Receive(Job(2), .11) == HandoffReply.Blocked && bridge.TakeIntent(.12) == null, "Healthy restored input cannot auto-recover/replay");
        Check(!bridge.AcknowledgeResult(Job().Key, bridge.ExecutorSession, .13), "Unknown result cannot be released through ordinary successful path");
        foreach (var kind in new[] { MotionReportKind.Stopped, MotionReportKind.Failed, MotionReportKind.Unknown })
        {
            bridge = New(); intent = Take(bridge); bridge.RecordDispatch(intent.Ticket, DispatchEvidence.Acknowledged, .03);
            bridge.Observe(new(intent.Ticket, .04, kind, true), .04);
            Check(bridge.Read(.04).ReconciliationRequired && bridge.Read(.04).Retained?.Stage.ToString() == kind.ToString(), "Non-completion retained for reconciliation: " + kind);
        }
        bridge = New(); intent = Take(bridge); bridge.RecordDispatch(intent.Ticket, DispatchEvidence.Acknowledged, .03);
        bridge.Observe(new(intent.Ticket, .04, MotionReportKind.Completed), .04);
        Check(bridge.Read(.04).Retained?.Stage == HandoffStage.Unknown, "Unqualified done flag cannot become business completion");
        bridge = New(); intent = Take(bridge); Finish(bridge, intent); bridge.Invalidate();
        Check(bridge.Read(.05).Retained?.Stage == HandoffStage.Completed && bridge.Read(.05).ReconciliationRequired, "Historical completion survives later link loss, but grants no new work");
        bridge = New(); bridge.Receive(Job(), .01); bridge.Invalidate();
        Check(bridge.Read(.02).Retained?.Stage == HandoffStage.NotSubmitted && bridge.IntentsTaken == 0, "Loss before intent take means no attempt through this model, not physical standstill");
        bridge = New(); intent = Take(bridge); bridge.Invalidate();
        Check(bridge.Read(.03).Retained?.Stage == HandoffStage.Unknown, "Loss after take preserves uncertain dispatch boundary");
        foreach (var bad in new[] { Input(.1) with { Valid = false }, Input(.1) with { RemoteAuto = false },
            Input(.1) with { ProcessPermit = false }, Input(.1) with { PlcBoot = Guid.NewGuid() }, Input(.3), Input(double.NaN), Input(0) })
        {
            bridge = New(); intent = Take(bridge); bridge.Update(bad, .1);
            Check(bridge.Read(.1).ReconciliationRequired && bridge.Read(.1).Retained?.Stage == HandoffStage.Unknown, "Invalid PLC input while dispatch is unresolved freezes publication/intent flow");
        }
        bridge = New(); bridge.Update(Input(.3), .3);
        Check(bridge.Read(.3).ReconciliationRequired, "Fresh sample after coverage gap cannot hide missing permission history");
        bridge = New(); bridge.Update(Input(.2), .3);
        Check(bridge.Read(.3).ReconciliationRequired, "Late processing of a fresh-enough sample cannot hide expired prior observation");
        bridge = New(); bridge.Receive(Job(), .01);
        Check(bridge.TakeIntent(.26) == null && bridge.Read(.26).Retained?.Stage == HandoffStage.NotSubmitted, "Freshness rechecked before intent take");
        bridge = New(); intent = Take(bridge);
        Check(!bridge.RecordDispatch(intent.Ticket, DispatchEvidence.Acknowledged, .26) && bridge.Read(.26).Retained?.Stage == HandoffStage.Unknown, "Expired PLC context during native-call window is unknown");
        bridge = New(); intent = Take(bridge); bridge.RecordDispatch(intent.Ticket, DispatchEvidence.Acknowledged, .03);
        bridge.Observe(new(new(Guid.NewGuid(), Guid.NewGuid()), .04, MotionReportKind.Completed, true), .04);
        Check(bridge.Read(.04).Retained?.Stage == HandoffStage.CallAcknowledged, "Foreign local execution ticket cannot complete current PLC job");
        foreach (double time in new[] { .02, .2, double.NaN })
        {
            bridge = New(); intent = Take(bridge); bridge.RecordDispatch(intent.Ticket, DispatchEvidence.Acknowledged, .03);
            bridge.Observe(new(intent.Ticket, time, MotionReportKind.Completed, true), .1);
            Check(bridge.Read(.1).Retained?.Stage == HandoffStage.Unknown, "Repeated/future/non-finite executor evidence refused");
        }
        bridge = New(); bridge.Receive(Job(ushort.MaxValue), .01); intent = bridge.TakeIntent(.02)!; Finish(bridge, intent);
        bridge.AcknowledgeResult(Job(ushort.MaxValue).Key, bridge.ExecutorSession, .05);
        Check(bridge.Receive(Job(1), .06) == HandoffReply.Stale, "Request id exhaustion never silently wraps");
        var presets = new ushort[] { 1, 2 }; bridge = new(Plc, 1, presets); presets[0] = 9; bridge.Update(Input(0), 0);
        Check(bridge.Receive(Job(), .01) == HandoffReply.Accepted, "Recipe preset whitelist copied, not externally mutable");
        bridge = New(); intent = Take(bridge); bridge.RecordDispatch(intent.Ticket, (DispatchEvidence)99, .03);
        Check(bridge.Read(.03).ReconciliationRequired, "Undefined dispatch evidence cannot leave a resumable attempt");
        bridge = New(); bridge.Read(double.NaN);
        Check(bridge.Read(.1).ReconciliationRequired, "Invalid clock locks context");
        var replacement = New();
        Check(replacement.ExecutorSession != New().ExecutorSession && replacement.Read(0).HighWater == 0, "New process-like instance has new session but no durable dedup history");
        Console.WriteLine($"{_count} offline PLC-motion handoff checks passed; no PLC network, LTDMC, axis or stop executed.");
    }
}
