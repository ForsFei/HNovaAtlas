namespace IndustrialRobotics;

public readonly record struct Vec3(double X, double Y, double Z)
{
    public Vec3 RequireFinite()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z))
            throw new ArgumentException("All three components must be finite.");
        return this;
    }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z).RequireFinite();
    public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z).RequireFinite();
}

public enum RotationAxis { X, Y, Z }

// Column vectors, right-handed axes, row-major storage. No vendor Euler decoder.
public sealed class Rotation3
{
    private readonly double[] values;
    private const double Tolerance = 1e-9;
    private Rotation3(double[] validated) => values = validated;
    public static Rotation3 Identity { get; } = FromRowMajor([1, 0, 0, 0, 1, 0, 0, 0, 1]);

    public static Rotation3 FromRowMajor(ReadOnlySpan<double> matrix)
    {
        if (matrix.Length != 9) throw new ArgumentException("A 3x3 matrix needs nine entries.");
        double[] m = matrix.ToArray();
        if (m.Any(x => !double.IsFinite(x))) throw new ArgumentException("Non-finite rotation.");
        for (int row = 0; row < 3; row++)
            for (int other = 0; other < 3; other++)
            {
                double dot = 0;
                for (int k = 0; k < 3; k++) dot += m[row * 3 + k] * m[other * 3 + k];
                double expected = row == other ? 1 : 0;
                if (!double.IsFinite(dot) || Math.Abs(dot - expected) > Tolerance)
                    throw new ArgumentException("Rotation must be orthonormal; no silent repair.");
            }
        double determinant = m[0] * (m[4] * m[8] - m[5] * m[7])
            - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (!double.IsFinite(determinant) || Math.Abs(determinant - 1) > Tolerance)
            throw new ArgumentException("A reflection is not a proper rotation.");
        return new Rotation3(m);
    }

    public static Rotation3 Around(RotationAxis axis, double radians)
    {
        if (!Enum.IsDefined(axis) || !double.IsFinite(radians) || Math.Abs(radians) > 2 * Math.PI)
            throw new ArgumentException("Teaching input: an explicit axis and radians within +/-2pi.");
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return axis switch
        {
            RotationAxis.X => FromRowMajor([1, 0, 0, 0, c, -s, 0, s, c]),
            RotationAxis.Y => FromRowMajor([c, 0, s, 0, 1, 0, -s, 0, c]),
            _ => FromRowMajor([c, -s, 0, s, c, 0, 0, 0, 1])
        };
    }

    public Vec3 Apply(Vec3 vector)
    {
        vector.RequireFinite();
        return new Vec3(values[0] * vector.X + values[1] * vector.Y + values[2] * vector.Z,
            values[3] * vector.X + values[4] * vector.Y + values[5] * vector.Z,
            values[6] * vector.X + values[7] * vector.Y + values[8] * vector.Z).RequireFinite();
    }

    // this * inner: inner is applied first.
    public Rotation3 Compose(Rotation3 inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        double[] result = new double[9];
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                for (int k = 0; k < 3; k++)
                    result[row * 3 + col] += values[row * 3 + k] * inner.values[k * 3 + col];
        return FromRowMajor(result);
    }

    public Rotation3 Inverse() => FromRowMajor([
        values[0], values[3], values[6], values[1], values[4], values[7], values[2], values[5], values[8]]);
    public double[] ToRowMajor() => (double[])values.Clone();
}

public readonly record struct FramedPoint(string Frame, Vec3 Millimeters);

// TargetFromSource: p_target = R * p_source + t (millimeters).
public sealed class FrameTransform
{
    public string Target { get; }
    public string Source { get; }
    public Rotation3 Rotation { get; }
    public Vec3 TranslationMm { get; }

    public FrameTransform(string target, string source, Rotation3 rotation, Vec3 translationMm)
    {
        ValidateName(target); ValidateName(source);
        ArgumentNullException.ThrowIfNull(rotation);
        Target = target; Source = source; Rotation = rotation;
        TranslationMm = translationMm.RequireFinite();
    }

    public FramedPoint ApplyPoint(FramedPoint point)
    {
        if (!string.Equals(point.Frame, Source, StringComparison.Ordinal))
            throw new ArgumentException("Point frame does not match transform source.");
        return new FramedPoint(Target, Rotation.Apply(point.Millimeters) + TranslationMm);
    }

    // Vectors have no origin: do not add TranslationMm. Caller supplies source-frame components.
    public Vec3 RotateVector(Vec3 sourceVector) => Rotation.Apply(sourceVector);

    public FrameTransform Compose(FrameTransform inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (!string.Equals(Source, inner.Target, StringComparison.Ordinal))
            throw new ArgumentException("Frame chain does not join.");
        return new FrameTransform(Target, inner.Source, Rotation.Compose(inner.Rotation),
            Rotation.Apply(inner.TranslationMm) + TranslationMm);
    }

    public FrameTransform Inverse()
    {
        Rotation3 inverse = Rotation.Inverse();
        return new FrameTransform(Source, Target, inverse, inverse.Apply(-TranslationMm));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim())
            throw new ArgumentException("Explicit, unpadded frame name required.");
    }
}
