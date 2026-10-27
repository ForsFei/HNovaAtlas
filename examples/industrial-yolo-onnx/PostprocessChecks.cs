namespace IndustrialYolo.Onnx;

internal static class PostprocessChecks
{
    private static int count;
    public static void Managed()
    {
        count = 0;
        Check(YoloXPostprocessor.GridAt(0) == new YoloXGrid(0, 0, 8), "first grid");
        Check(YoloXPostprocessor.GridAt(53) == new YoloXGrid(1, 1, 8), "x-fast grid order");
        Check(YoloXPostprocessor.GridAt(2703) == new YoloXGrid(51, 51, 8), "stride8 last row");
        Check(YoloXPostprocessor.GridAt(2704) == new YoloXGrid(0, 0, 16), "stride16 first row");
        Check(YoloXPostprocessor.GridAt(3379) == new YoloXGrid(25, 25, 16), "stride16 last row");
        Check(YoloXPostprocessor.GridAt(3380) == new YoloXGrid(0, 0, 32), "stride32 first row");
        Check(YoloXPostprocessor.GridAt(3548) == new YoloXGrid(12, 12, 32), "last grid");
        float[] raw = Empty();
        PostprocessReport empty = Run(raw);
        Check(empty is { Filtered: 3549, Visible: 0 } && empty.Candidates.Count == 0 && empty.Detections.Count == 0, "zero scores legitimate empty result, strict >0.1");
        Check(empty.Rows[0].NetworkBox == new Box(-4, -4, 4, 4), "raw zero means width8, not xyxy zeros");
        Check(empty.Rows[2704].NetworkBox == new Box(-8, -8, 8, 8), "stride16 golden decode");
        Check(empty.Rows[3380].NetworkBox == new Box(-16, -16, 16, 16), "stride32 golden decode");
        Check(empty.Rows[3548].NetworkBox == new Box(368, 368, 400, 400), "last grid golden coordinates");
        Set(raw, 53, 12, 12, 16, 24, .8f, 7, .5f);
        PostprocessReport one = Run(raw);
        DecodedYoloXRow row = one.Candidates.Single();
        Check(row.ClassId == 7 && row.RowIndex == 53 && row.Grid == new YoloXGrid(1, 1, 8), "row/class identity preserved");
        Near(row.NetworkBox.X1, 4, "exp width + grid x");
        Near(row.NetworkBox.Y1, 0, "exp height + grid y");
        Near(row.NetworkBox.X2, 20, "cxcywh to right edge");
        Near(row.NetworkBox.Y2, 24, "cxcywh to bottom edge");
        Check(row.Score == .8f * .5f && row.Objectness == .8f && row.ClassProbability == .5f, "objectness product, no repeated sigmoid");
        Check(one.Visible == 1 && one.Suppressions.Count == 0 && !one.Detections[0].Source.WasClipped, "complete source box");
        raw[53 * 85] = 100;
        Check(one.Rows[53].NetworkBox == row.NetworkBox, "report detached from caller raw array");
        Reject(() => ((IList<DecodedYoloXRow>)one.Rows).Clear(), "decoded collection read-only");
        Reject(() => ((IList<YoloXDetection>)one.Detections).Clear(), "detections read-only");
        float[] equality = Empty(); Set(equality, 0, 20, 20, 8, 8, .5f, 0, .5f);
        Check(Run(equality, .25f).Candidates.Count == 0, "score threshold equality excluded, unlike first-batch evaluator");
        Check(Run(equality, .249f).Candidates.Count == 1, "score just above threshold included");
        float[] labelTie = Empty(); Set(labelTie, 0, 20, 20, 8, 8, 1, 2, .8f); labelTie[8] = .8f;
        Check(Run(labelTie).Candidates.Single().ClassId == 2, "class score tie chooses first class");
        labelTie[4] = 0; labelTie[5 + 7] = 1;
        Check(Run(labelTie, 0).Rows[0].ClassId == 0 && Run(labelTie, 0).Candidates.Count == 0, "argmax applies product, zero objectness ties all classes");
        float[] duplicates = Empty();
        Set(duplicates, 0, 50, 50, 20, 20, 1, 0, .9f); Set(duplicates, 1, 50, 50, 20, 20, 1, 1, .8f);
        PostprocessReport duplicateReport = Run(duplicates);
        Check(duplicateReport.Candidates.Count == 2 && duplicateReport.Detections.Count == 1, "class-agnostic suppression crosses classes");
        Check(duplicateReport.Suppressions.Single() is { RowIndex: 1, KeptRowIndex: 0, Overlap: 1 }, "suppression ledger identifies retaining row");
        Check(Run(duplicates, nms: 1).Detections.Count == 2, "IoU equality retained, suppress only >threshold");
        duplicates[1 * 85 + 5 + 1] = .9f;
        Check(Run(duplicates).Detections.Single().Candidate.RowIndex == 0, "equal candidate scores explicitly prefer smaller row");
        float[] distinct = Empty(); Set(distinct, 0, 20, 20, 8, 8, 1, 0, .8f); Set(distinct, 2, 100, 100, 8, 8, 1, 0, .9f);
        PostprocessReport order = Run(distinct);
        Check(order.Candidates.Select(r => r.RowIndex).SequenceEqual([0, 2]) && order.Detections.Select(d => d.Candidate.RowIndex).SequenceEqual([2, 0]), "candidate input order vs kept score order");
        Check(Run(distinct, nms: 0).Detections.Count == 2, "zero overlap not suppressed at nms0");
        Box first = new(0, 0, 1, 1), touching = new(1, 0, 2, 1);
        Near(BoxMath.IoU(first, touching), 0, "evaluation continuous IoU");
        Near(YoloXPostprocessor.UpstreamInclusiveNmsIou(first, touching), 1d / 3, "legacy NMS +1 differs on tiny touching boxes");
        Near(YoloXPostprocessor.UpstreamInclusiveNmsIou(first, first), 1, "identical NMS IoU");
        Near(YoloXPostprocessor.UpstreamInclusiveNmsIou(first, new(3, 3, 4, 4)), 0, "disjoint NMS IoU");
        float[] padding = Empty(); Set(padding, 0, 100, 300, 20, 20, 1, 0, .9f);
        PostprocessReport paddingReport = Run(padding);
        Check(paddingReport.Detections.Count == 1 && paddingReport.Visible == 0 && paddingReport.Detections[0].Source.Original is null, "padding survivor retained in audit, not fabricated source box");
        Check(YoloXPostprocessor.ClipNoResize(new(-10, 200, 20, 220), 208) == new NoResizeSourceBox(new(0, 200, 20, 208), true), "partial clip golden source coordinates");
        Check(YoloXPostprocessor.ClipNoResize(new(0, 208, 10, 220), 208).Original is null, "touching source boundary has zero continuous area");
        float[] beforeClip = Empty(); Set(beforeClip, 0, 5, 10, 10, 20, 1, 0, .9f); Set(beforeClip, 1, 5, -40, 10, 120, 1, 0, .8f);
        Check(Run(beforeClip).Detections.Count == 2, "NMS before clipping, even when later source boxes coincide");
        Check(duplicateReport.Filtered + duplicateReport.Candidates.Count == 3549 && duplicateReport.Detections.Count + duplicateReport.Suppressions.Count == duplicateReport.Candidates.Count, "stage accounting");
        Check(YoloXPostprocessor.ClipNoResize(new(10, 400, 20, 416), 416).Original == new Box(10, 400, 20, 416), "full-height r1 source has no bottom padding");
        Reject(() => YoloXPostprocessor.GridAt(-1), "negative grid row");
        Reject(() => YoloXPostprocessor.GridAt(3549), "grid upper bound");
        Reject(() => YoloXPostprocessor.ProcessValues(new float[84], 208, new()), "wrong layout rejected");
        Reject(() => YoloXPostprocessor.ProcessValues(Empty(), 0, new()), "zero source height");
        Reject(() => YoloXPostprocessor.ProcessValues(Empty(), 417, new()), "source height upper bound");
        Reject(() => YoloXPostprocessor.ProcessValues(Empty(), 208, null!), "null settings");
        Reject(() => Run(Empty(), float.NaN), "NaN score threshold");
        Reject(() => Run(Empty(), -.1f), "negative score threshold");
        Reject(() => Run(Empty(), 1.1f), "score threshold >1");
        Reject(() => Run(Empty(), nms: double.PositiveInfinity), "nonfinite NMS threshold");
        Reject(() => Run(Empty(), nms: -1), "negative NMS threshold");
        Reject(() => Run(Empty(), nms: 1.1), "NMS threshold >1");
        float[] invalid = Empty(); invalid[84] = float.NaN;
        Reject(() => Run(invalid), "nonfinite filtered row cannot be silently dropped");
        invalid[84] = 2; Reject(() => Run(invalid), "class logits not accepted as probabilities");
        invalid[84] = -.1f; Reject(() => Run(invalid), "negative probability rejected");
        invalid[84] = 0; invalid[4] = 2; Reject(() => Run(invalid), "objectness range");
        invalid[4] = 0; invalid[2] = 1000; Reject(() => Run(invalid), "exp overflow rejected");
        invalid[2] = -1000; Reject(() => Run(invalid), "exp underflow/zero width rejected");
        invalid[2] = 0; invalid[0] = 1e9f; Reject(() => Run(invalid), "teaching geometry bound");
        Reject(() => YoloXPostprocessor.UpstreamInclusiveNmsIou(new(1, 0, 0, 1), first), "inverted NMS box");
        Reject(() => YoloXPostprocessor.ClipNoResize(null!, 208), "null network box");
        Reject(() => YoloXPostprocessor.ClipNoResize(new(0, 0, double.Epsilon, double.Epsilon), 208), "underflow network area rejected");
        Reject(() => YoloXPostprocessor.ClipNoResize(new(0, 207.99999999999997, double.Epsilon, 220), 208), "clipped source area underflow rejected");
        Reject(() => YoloXPostprocessor.Process(null!, YoloXInput.PrepareNoResize(YoloXInput.CreateFixture()), new()), "null raw output");
        Console.WriteLine($"PASS {count} handwritten postprocess checks. No model inference executed by this command.");
    }

    public static void Native(string path)
    {
        count = 0;
        using YoloXNanoSession session = new(path);
        YoloXInput input = YoloXInput.PrepareNoResize(YoloXInput.CreateFixture());
        YoloXRawOutput raw = session.Run(input);
        float[] snapshot = raw.CopyValues();
        NumericReference.CheckSamples(raw);
        Check(true, "actual raw output still agrees with third-batch reference");
        PostprocessReport ordinary = YoloXPostprocessor.Process(raw, input, new());
        Check(ordinary.Candidates.Count == 0 && ordinary.Detections.Count == 0, "actual gradient below default0.1, not failure");
        PostprocessReport low = YoloXPostprocessor.Process(raw, input, new(.01f, .45));
        Check(low.Rows.Count == 3549, "all actual rows decoded");
        Check(low.Candidates.Count == 36, "actual numerical fixture candidates at0.01");
        Check(low.Detections.Select(d => d.Candidate.RowIndex).SequenceEqual([3453, 3436, 3041]), "actual NMS rows agree with NumPy reference");
        Check(low.Suppressions.Count == 33, "actual suppression accounting");
        Check(low.Visible == 3, "actual retained boxes intersect r1 source");
        Check(raw.Values.SequenceEqual(snapshot), "decode does not mutate native output snapshot");
        Check(input.Sha256 == NumericReference.InputSha256, "postprocess does not mutate input");
        Check(low.Detections.All(d => d.Source.Original is { X1: >= 0, Y1: >= 0, X2: <= 416, Y2: <= 208 }), "actual source bounds");
        Check(low.Candidates.All(d => d.Score > .01f && d.ClassId is >= 0 and < 80), "actual candidate scores/classes");
        session.Dispose();
        Check(low.Detections[0].Source.Original is not null, "managed detections survive session disposal");
        Console.WriteLine($"PASS {count} actual CPU+postprocess checks. Gradient responses are not real objects or detection-quality evidence.");
    }

    private static float[] Empty() => new float[3549 * 85];
    private static PostprocessReport Run(float[] raw, float score = .1f, double nms = .45) => YoloXPostprocessor.ProcessValues(raw, 208, new(score, nms));
    private static void Set(float[] raw, int row, float cx, float cy, float width, float height, float obj, int classId, float probability)
    {
        YoloXGrid grid = YoloXPostprocessor.GridAt(row); int i = row * 85;
        raw[i] = cx / grid.Stride - grid.X; raw[i + 1] = cy / grid.Stride - grid.Y;
        raw[i + 2] = MathF.Log(width / grid.Stride); raw[i + 3] = MathF.Log(height / grid.Stride);
        raw[i + 4] = obj; raw[i + 5 + classId] = probability;
    }
    private static void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) <= 1e-5, name);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Postprocess check failed: {name}");
        count++;
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { count++; return; }
        throw new InvalidOperationException($"Expected postprocess rejection: {name}");
    }
}
