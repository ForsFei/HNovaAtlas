using System.Numerics;

Energy total = Sum<Energy>([new(1.25m), new(2.5m)]);
Check(total.KilowattHours == 3.75m, "custom static contract sums quantities");
Check(Mean<int>([1, 2]) == 1, "integer division truncates");
Check(Mean<decimal>([1m, 2m]) == 1.5m, "decimal retains the fraction");
Check(Mean<decimal>([]) is null, "empty input has no mean");
Console.WriteLine("静态抽象契约与泛型数值边界断言全部通过。");

static T Sum<T>(IEnumerable<T> values) where T : IAdditiveQuantity<T>
{
    T total = T.Zero;
    foreach (T value in values)
        total += value;
    return total;
}

static T? Mean<T>(IReadOnlyList<T> values) where T : struct, INumber<T>
{
    if (values.Count == 0) return null;
    T total = T.Zero;
    foreach (T value in values)
        total += value;
    return total / T.CreateChecked(values.Count);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public interface IAdditiveQuantity<TSelf> where TSelf : IAdditiveQuantity<TSelf>
{
    static abstract TSelf Zero { get; }
    static abstract TSelf operator +(TSelf left, TSelf right);
}

public readonly record struct Energy : IAdditiveQuantity<Energy>
{
    public decimal KilowattHours { get; }

    public Energy(decimal kilowattHours)
    {
        if (kilowattHours < 0) throw new ArgumentOutOfRangeException(nameof(kilowattHours));
        KilowattHours = kilowattHours;
    }

    public static Energy Zero => new(0m);
    public static Energy operator +(Energy left, Energy right)
        => new(left.KilowattHours + right.KilowattHours);
}
