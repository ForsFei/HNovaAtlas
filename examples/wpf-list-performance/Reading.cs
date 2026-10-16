namespace WpfListPerformance;

public sealed record Reading(int Id, string Device, int Temperature, bool IsAlert);
