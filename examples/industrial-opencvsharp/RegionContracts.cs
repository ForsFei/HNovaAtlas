namespace IndustrialOpenCvSharp;

public sealed record RegionRecipe(int Connectivity = 8, int MinimumArea = 2, int MaximumArea = 1000,
    int OriginX = 0, int OriginY = 0, bool RejectBorder = false)
{
    public void Validate()
    {
        if (Connectivity is not 4 and not 8 || MinimumArea < 1 || MaximumArea < MinimumArea || MaximumArea > 1_048_576 ||
            OriginX is < -1_000_000 or > 1_000_000 || OriginY is < -1_000_000 or > 1_000_000)
            throw new ArgumentException("Teaching connectivity4/8, positive bounded pixel area and bounded origin required.");
    }
}
public static class RegionContracts
{
    public static int[] Depths(int[] parents)
    {
        ArgumentNullException.ThrowIfNull(parents);
        if (parents.Length > 256) throw new ArgumentException("Teaching contour cap exceeded.");
        int[] depths = new int[parents.Length];
        for (int i = 0; i < parents.Length; i++)
        {
            int p = parents[i], depth = 0;
            while (p != -1)
            {
                if (p < 0 || p >= parents.Length || ++depth > parents.Length) throw new ArgumentException("Invalid parent index or cycle.");
                p = parents[p];
            }
            depths[i] = depth;
        }
        return depths;
    }
    public static void Checks()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        RegionRecipe r = new(); r.Validate(); Check(true, "Default bounded pixel recipe");
        (r with { Connectivity = 4 }).Validate(); Check(true, "Four-connectivity allowed");
        Reject(() => (r with { Connectivity = 6 }).Validate(), "Unknown connectivity refused");
        Reject(() => (r with { MinimumArea = 0 }).Validate(), "Nonpositive minimum refused");
        Reject(() => (r with { MaximumArea = 1 }).Validate(), "Reversed area interval refused");
        Reject(() => (r with { MaximumArea = 1_048_577 }).Validate(), "Teaching area cap enforced");
        Reject(() => (r with { OriginX = int.MaxValue }).Validate(), "Origin X bound enforced");
        Reject(() => (r with { OriginY = int.MinValue }).Validate(), "Origin Y bound enforced");
        Check(Depths([]).Length == 0, "No contours is valid empty hierarchy");
        Check(Depths([-1, -1]).SequenceEqual(new[] { 0, 0 }), "Independent roots have depth zero");
        Check(Depths([-1, 0, 1]).SequenceEqual(new[] { 0, 1, 2 }), "Nested island depth retained, not all children called holes");
        Check(Depths([2, 0, -1]).SequenceEqual(new[] { 1, 2, 0 }), "Parent need not precede child in array");
        Reject(() => Depths([0]), "Self-cycle refused"); Reject(() => Depths([1, 0]), "Two-node cycle refused");
        Reject(() => Depths([3]), "Out-of-range parent refused"); Reject(() => Depths([-2]), "Only minus-one is accepted as absent parent");
        Reject(() => Depths(new int[257]), "Teaching contour cap enforced");
        Reject(() => Depths(null!), "Missing hierarchy refused");
        Console.WriteLine($"Region managed checks passed: {count}. Recipe/parent graph only; no labeling or contour extraction.");
    }
}
