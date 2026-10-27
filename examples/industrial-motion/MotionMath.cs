namespace IndustrialMotion;

internal sealed record UnitScale
{
    public decimal CountsPerMillimetre { get; }
    public UnitScale(decimal countsPerMillimetre)
    {
        if (countsPerMillimetre <= 0) throw new ArgumentOutOfRangeException(nameof(countsPerMillimetre));
        CountsPerMillimetre = countsPerMillimetre;
    }
    public int ToCount(decimal millimetres) => checked((int)decimal.Round(
        checked(millimetres * CountsPerMillimetre), 0, MidpointRounding.AwayFromZero));
    public decimal ToMillimetres(int counts) => counts / CountsPerMillimetre;
}

// Ideal zero-start/zero-end-velocity point move only. NOT a Leadshine planner or a servo model.
internal sealed class TrapezoidPlan
{
    private readonly double _distance, _acceleration, _deceleration, _accelTime, _cruiseTime, _accelDistance;
    public double PeakVelocity { get; }
    public double Duration { get; }
    public bool IsTriangular => _cruiseTime <= 1e-10;

    public TrapezoidPlan(double distance, double velocity, double acceleration, double deceleration)
    {
        if (!double.IsFinite(distance) || distance <= 0 ||
            !double.IsFinite(velocity) || velocity <= 0 ||
            !double.IsFinite(acceleration) || acceleration <= 0 ||
            !double.IsFinite(deceleration) || deceleration <= 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        _distance = distance; _acceleration = acceleration; _deceleration = deceleration;
        PeakVelocity = Math.Min(velocity, Math.Sqrt(2 * distance / (1 / acceleration + 1 / deceleration)));
        _accelTime = PeakVelocity / acceleration;
        _accelDistance = PeakVelocity * _accelTime / 2;
        double decelDistance = PeakVelocity * PeakVelocity / (2 * deceleration);
        _cruiseTime = Math.Max(0, distance - _accelDistance - decelDistance) / PeakVelocity;
        Duration = _accelTime + _cruiseTime + PeakVelocity / deceleration;
        if (!double.IsFinite(Duration) || !double.IsFinite(PeakVelocity) || Duration <= 0 || PeakVelocity <= 0)
            throw new ArgumentOutOfRangeException(nameof(distance), "Unrepresentable profile.");
    }

    public (double Distance, double Velocity) Sample(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds >= Duration) return (_distance, 0);
        if (seconds < _accelTime) return (_acceleration * seconds * seconds / 2, _acceleration * seconds);
        if (seconds < _accelTime + _cruiseTime)
            return (_accelDistance + PeakVelocity * (seconds - _accelTime), PeakVelocity);
        double remaining = Duration - seconds;
        return (_distance - _deceleration * remaining * remaining / 2, _deceleration * remaining);
    }
}
