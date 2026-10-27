namespace IndustrialOpenCvSharp;

public sealed record ShapeRules(double MinimumArea = 10, double MinimumSolidity = .9,
    double MinimumBoxFill = .85, double MaximumAspect = 5, double EpsilonRatio = .02,
    double SquareRatio = 1.05, bool RejectChildren = true)
{
    public void Validate()
    {
        if (!double.IsFinite(MinimumArea) || MinimumArea is <= 0 or > 1_000_000 ||
            !double.IsFinite(MinimumSolidity) || MinimumSolidity is < 0 or > 1 ||
            !double.IsFinite(MinimumBoxFill) || MinimumBoxFill is < 0 or > 1 ||
            !double.IsFinite(MaximumAspect) || MaximumAspect is < 1 or > 100 ||
            !double.IsFinite(EpsilonRatio) || EpsilonRatio is <= 0 or > .1 ||
            !double.IsFinite(SquareRatio) || SquareRatio is < 1 or > 1.2)
            throw new ArgumentException("Finite bounded teaching shape thresholds required.");
    }
}
public static class ShapeContracts
{
    public static double AxisDegrees(double degrees)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentException("Finite degree angle required.");
        double value = degrees % 180;
        if (value >= 90) value -= 180;
        if (value < -90) value += 180;
        return value;
    }
    public static double? Direction(double dx, double dy, double longSide, double shortSide, double squareRatio)
    {
        if (new[] { dx, dy, longSide, shortSide, squareRatio }.Any(v => !double.IsFinite(v)) ||
            shortSide <= 0 || longSide < shortSide || squareRatio < 1 || dx * dx + dy * dy < 1e-12)
            throw new ArgumentException("Nondegenerate finite rectangle axis required.");
        return longSide / shortSide <= squareRatio ? null : AxisDegrees(Math.Atan2(dy, dx) * 180 / Math.PI);
    }
    public static void Checks()
    {
        int count = 0;
        void Check(bool v, string label) { if (!v) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        ShapeRules r = new(); r.Validate(); Check(r.RejectChildren, "Default child-bearing roots not silently accepted");
        Reject(() => (r with { MinimumArea = 0 }).Validate(), "Nonpositive area refused");
        Reject(() => (r with { MinimumSolidity = 1.1 }).Validate(), "Solidity threshold bound");
        Reject(() => (r with { MinimumBoxFill = double.NaN }).Validate(), "Nonfinite fill refused");
        Reject(() => (r with { MaximumAspect = .9 }).Validate(), "Aspect below one refused");
        Reject(() => (r with { EpsilonRatio = 0 }).Validate(), "Zero simplification ratio refused");
        Reject(() => (r with { SquareRatio = 1.3 }).Validate(), "Ambiguity policy bound");
        Check(AxisDegrees(180) == 0 && AxisDegrees(-180) == 0, "Axis direction has180-degree symmetry");
        Check(AxisDegrees(90) == -90 && AxisDegrees(270) == -90, "Normalized axis interval is minus90 inclusive to90 exclusive");
        Check(AxisDegrees(-91) == 89, "Wrap across interval boundary");
        Reject(() => AxisDegrees(double.NaN), "Nonfinite angle refused");
        Check(Direction(1, 0, 10, 4, 1.05) == 0 && Direction(-1, 0, 10, 4, 1.05) == 0, "Reversed long edge same axis");
        Check(Direction(1, 1, 10, 4, 1.05) == 45, "Image downward axis convention explicit");
        Check(Direction(1, 0, 10, 10, 1.05) is null && Direction(1, 0, 10.4, 10, 1.05) is null, "Square and near square withhold unique long-axis angle");
        Reject(() => Direction(0, 0, 10, 4, 1.05), "Zero direction vector refused");
        Reject(() => Direction(1, 0, 10, 0, 1.05), "Degenerate short side refused");
        Console.WriteLine($"Shape managed checks passed: {count}. Rules/axis only, not fitting or classification.");
    }
}
