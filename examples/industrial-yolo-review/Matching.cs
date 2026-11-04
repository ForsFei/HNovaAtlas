using IndustrialYolo;

namespace IndustrialYolo.Review;

public sealed record ReviewPrediction(int Row, int ClassId, float Score, Box? Box, bool WasClipped);
public sealed record ReviewDecision(ReviewPrediction Prediction, string Decision, int? TruthIndex, double? MatchedIou);
public sealed record Counts(int Tp, int Fp, int Fn)
{
    public double? Precision => Tp + Fp == 0 ? null : (double)Tp / (Tp + Fp);
    public double? Recall => Tp + Fn == 0 ? null : (double)Tp / (Tp + Fn);
}
public sealed record CaseEvaluation(Counts Counts, IReadOnlyList<ReviewDecision> Decisions, IReadOnlyList<int> MissedTruth);

public static class Matching
{
    // Fixed operating point after score filtering and NMS, continuous-area IoU (NO +1).
    public static CaseEvaluation Evaluate(int width, int height, IReadOnlyList<Truth> truth,
        IReadOnlyList<ReviewPrediction> predictions, double threshold)
    {
        IndustrialYolo.Images.ImageGeometry.ValidateDimensions(width, height);
        ReviewData.Require(double.IsFinite(threshold) && threshold is > 0 and <= 1 && truth.Count <= 128 && predictions.Count <= 3549, "Invalid matching limits.");
        foreach (Truth item in truth) { ReviewData.Require(item.ClassId is >= 0 and < 80, "Invalid truth class."); BoxMath.Validate(item.Box, width, height); }
        HashSet<int> rows = [];
        foreach (ReviewPrediction p in predictions)
        {
            ReviewData.Require(p.Row is >= 0 and < 3549 && rows.Add(p.Row) && p.ClassId is >= 0 and < 80 &&
                float.IsFinite(p.Score) && p.Score is >= 0 and <= 1, "Invalid/duplicate prediction row.");
            if (p.Box is not null) BoxMath.Validate(p.Box, width, height);
        }
        bool[] used = new bool[truth.Count]; List<ReviewDecision> decisions = [];
        int tp = 0, fp = 0;
        foreach (ReviewPrediction p in predictions.OrderByDescending(p => p.Score).ThenBy(p => p.Row))
        {
            if (p.Box is null) { decisions.Add(new(p, "ExcludedPadding", null, null)); continue; }
            int best = -1; double bestIou = -1;
            for (int i = 0; i < truth.Count; i++)
            {
                if (used[i] || truth[i].ClassId != p.ClassId) continue;
                double overlap = BoxMath.IoU(p.Box, truth[i].Box);
                if (overlap > bestIou) { best = i; bestIou = overlap; }
            }
            if (best >= 0 && bestIou >= threshold)
            { used[best] = true; tp++; decisions.Add(new(p, "TP", best, bestIou)); }
            else { fp++; decisions.Add(new(p, "FP", null, null)); }
        }
        int[] missed = Enumerable.Range(0, truth.Count).Where(i => !used[i]).ToArray();
        return new(new(tp, fp, missed.Length), Array.AsReadOnly(decisions.ToArray()), Array.AsReadOnly(missed));
    }
}
