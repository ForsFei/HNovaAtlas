namespace IndustrialMotion;

internal static class RecoveryChecks
{
    private static int _count;
    private static readonly Guid Connection = Guid.NewGuid();
    private static InterruptedRun Original => new(Guid.NewGuid(), "part-A", true, true);
    private static RecoveryFrame Complete(double time) => new(time, Connection, 1, 1, "part-A",
        RecoveryReview.AllRequired, WorkpieceDisposition.RetainForNewPlan);
    private static RecoveryReview New() => new(Original);
    private static RecoveryReviewKey Key(RecoveryReview review, double time) => review.Read(time).Key ?? throw new InvalidOperationException("No key.");
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static void InvalidConstructor(InterruptedRun original, double age, string label)
    {
        try { _ = new RecoveryReview(original, age); } catch (ArgumentException) { Check(true, label); return; }
        throw new InvalidOperationException("Expected argument rejection: " + label);
    }
    public static void Demo()
    {
        var review = New();
        review.Update(Complete(.1) with { Facts = RecoveryReview.AllRequired & ~RecoveryFact.ShotReconciled }, .1);
        Console.WriteLine($"Missing shot reconciliation: {review.Read(.1).Missing}; can offer review={review.Read(.1).CanOfferNewPlanReview}");
        review.Update(Complete(.2), .2);
        var key = Key(review, .2);
        Console.WriteLine($"Explicit human-review receipt recorded={review.AcknowledgeForNewPlanReview(key, .21)}; no SDK call or new plan created");
        review.Update(Complete(.3) with { ReferenceRevision = 2 }, .3);
        Console.WriteLine($"Reference changed: old receipt current={review.Read(.3).CurrentReviewAcknowledged}, old-key reuse={review.AcknowledgeForNewPlanReview(key, .31)}");
        Console.WriteLine($"Original motion/shot uncertainty retained={review.Original.MotionMayHaveExecuted}/{review.Original.ShotMayHaveOccurred}. No resume/trigger/reset/hardware.");
    }
    public static void Run()
    {
        var review = New(); var view = review.Read(0);
        Check(view.Key == null && view.Missing == RecoveryReview.AllRequired && !view.CanOfferNewPlanReview, "No review readiness before evidence");
        Check(review.Original.MotionMayHaveExecuted && review.Original.ShotMayHaveOccurred, "Original ambiguous effects kept as history");
        foreach (var fact in Enum.GetValues<RecoveryFact>().Where(f => f != RecoveryFact.None))
        {
            review = New(); review.Update(Complete(.1) with { Facts = RecoveryReview.AllRequired & ~fact }, .1);
            view = review.Read(.1);
            Check(view.Missing == fact && !view.CanOfferNewPlanReview, "Each independently missing fact prevents review offer: " + fact);
        }
        review = New(); review.Update(Complete(.1) with { Disposition = WorkpieceDisposition.Unresolved }, .1);
        Check(!review.Read(.1).CanOfferNewPlanReview, "Facts alone cannot decide physical workpiece disposition");
        review = New(); review.Update(Complete(.1), .1); view = review.Read(.1);
        Check(view.Key.HasValue && view.CanOfferNewPlanReview && !view.CurrentReviewAcknowledged, "Complete fixture only offers human review, not automatic acknowledgement");
        var key = Key(review, .1);
        Check(!review.AcknowledgeForNewPlanReview(new(Guid.NewGuid(), Guid.NewGuid()), .11), "Foreign review key refused");
        Check(review.AcknowledgeForNewPlanReview(key, .12), "Explicit acknowledgement records one review receipt");
        Check(review.Read(.12).CurrentReviewAcknowledged && !review.Read(.12).CanOfferNewPlanReview, "Acknowledged view does not repeatedly offer the same review");
        Check(!review.AcknowledgeForNewPlanReview(key, .13), "Duplicate acknowledgement cannot replay receipt");
        Check(review.LastAcknowledgement?.Original == review.Original && review.LastAcknowledgement.Basis.SampledAt == .1, "Receipt preserves original uncertainty and exact reviewed evidence");
        review.Update(Complete(.2), .2);
        Check(Key(review, .2) == key && review.Read(.2).CurrentReviewAcknowledged, "Fresh unchanged substantive basis preserves acknowledgement");
        review = New(); review.Update(Complete(.1), .1); key = Key(review, .1); review.Update(Complete(.2), .2);
        Check(Key(review, .2) == key && review.AcknowledgeForNewPlanReview(key, .21), "Fresh unchanged basis avoids invalidating ordinary UI refresh");
        foreach (var changed in new[] { Complete(.2) with { ConnectionEpoch = Guid.NewGuid() }, Complete(.2) with { ConfigurationRevision = 2 },
            Complete(.2) with { ReferenceRevision = 2 }, Complete(.2) with { Disposition = WorkpieceDisposition.RemovedOrQuarantined },
            Complete(.2) with { Facts = RecoveryReview.AllRequired & ~RecoveryFact.TriggerQuiescent } })
        {
            review = New(); review.Update(Complete(.1), .1); key = Key(review, .1);
            review.AcknowledgeForNewPlanReview(key, .11); review.Update(changed, .2);
            Check(Key(review, .2) != key && !review.Read(.2).CurrentReviewAcknowledged && !review.AcknowledgeForNewPlanReview(key, .21), "Basis change invalidates old UI confirmation: " + changed);
            Check(review.LastAcknowledgement?.Key == key && review.Original.MotionMayHaveExecuted, "Previous receipt remains history, never current permission");
        }
        review = New(); review.Update(Complete(.1), .1); key = Key(review, .1);
        review.Update(Complete(.2) with { ReferenceRevision = 2 }, .2);
        Check(review.AcknowledgeForNewPlanReview(Key(review, .2), .21), "Changed but complete basis requires a new explicit review");
        review = New(); review.Update(Complete(.1) with { Disposition = WorkpieceDisposition.RemovedOrQuarantined }, .1);
        Check(review.AcknowledgeForNewPlanReview(Key(review, .1), .11) && review.LastAcknowledgement?.Basis.Workpiece == "part-A", "Removed/quarantined disposition accounts for old part, does not create replacement job");
        var other = New(); other.Update(Complete(.1), .1);
        review = New(); review.Update(Complete(.1), .1);
        Check(!review.AcknowledgeForNewPlanReview(Key(other, .1), .11), "Equal-looking other instance cannot confirm this review");
        foreach (var bad in new[] { Complete(.2) with { Valid = false }, Complete(.2) with { Workpiece = "part-B" },
            Complete(.2) with { ConnectionEpoch = Guid.Empty }, Complete(.2) with { ConfigurationRevision = 0 },
            Complete(.2) with { ReferenceRevision = 0 }, Complete(.2) with { Facts = (RecoveryFact)1023 },
            Complete(.2) with { Disposition = (WorkpieceDisposition)99 }, Complete(.2) with { SampledAt = double.NaN },
            Complete(.2) with { SampledAt = .3 }, Complete(.2) with { SampledAt = -.1 }, Complete(.2) with { SampledAt = .1 } })
        {
            review = New(); review.Update(Complete(.1), .1); key = Key(review, .1); review.AcknowledgeForNewPlanReview(key, .11);
            review.Update(bad, .2); view = review.Read(.2);
            Check(view.Key == null && !view.CurrentReviewAcknowledged && !view.CanOfferNewPlanReview && review.LastAcknowledgement?.Key == key,
                "Invalid evidence clears current acknowledgement, not historical receipt: " + bad);
        }
        review = New(); review.Update(Complete(0), 0); key = Key(review, 0);
        Check(review.Read(.25).CanOfferNewPlanReview, "Exactly-at-age-boundary evidence allowed by explicit policy");
        Check(!review.AcknowledgeForNewPlanReview(key, .2501) && review.Read(.2501).Key == null, "Stale UI acknowledgement refused at use time");
        review.Update(Complete(.3), .3);
        Check(Key(review, .3) != key && !review.Read(.3).CurrentReviewAcknowledged, "Fresh data after expiry cannot revive old key");
        review = New(); review.Update(Complete(.1), .1); key = Key(review, .1); review.AcknowledgeForNewPlanReview(key, .11);
        review.Update(Complete(.4), .4);
        Check(Key(review, .4) != key && !review.Read(.4).CurrentReviewAcknowledged, "Unobserved freshness gap invalidates old confirmation even with unchanged values");
        review = New(); review.Update(Complete(.01), .5);
        Check(review.Read(.5).Key == null, "New receipt time does not freshen old source sample");
        review = New(); review.Update(Complete(.1), .1); key = Key(review, .1); review.AcknowledgeForNewPlanReview(key, .11);
        review.Invalidate(); view = review.Read(.12);
        Check(view.Key == null && !view.CurrentReviewAcknowledged && review.LastAcknowledgement?.Key == key, "Explicit invalidation revokes current review, sends no stop/reset");
        review.Update(Complete(.2), .2);
        Check(Key(review, .2) != key && !review.Read(.2).CurrentReviewAcknowledged, "Recovered inputs do not acknowledge themselves");
        foreach (double badNow in new[] { -.1, double.NaN, double.PositiveInfinity, .05 })
        {
            review = New(); review.Update(Complete(.1), .1); key = Key(review, .1);
            Check(!review.AcknowledgeForNewPlanReview(key, badNow) && review.Read(.2).Key == null, "Invalid/backwards clock removes current review: " + badNow);
        }
        InvalidConstructor(Original with { RunId = Guid.Empty }, .25, "Interrupted run requires identity");
        InvalidConstructor(Original with { Workpiece = " " }, .25, "Workpiece label cannot be empty");
        InvalidConstructor(Original, 0, "Invalid age policy rejected");
        InvalidConstructor(Original, double.NaN, "Non-finite age policy rejected");
        Console.WriteLine($"{_count} offline recovery-review checks passed. No new plan, resume, stop, reset, trigger or hardware.");
    }
}
