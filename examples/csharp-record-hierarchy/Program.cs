int[] samples = [10, 20];
DeviceEvent reading = new ReadingEvent("pump-A", 72, samples);
DeviceEvent same = new ReadingEvent("pump-A", 72, samples);
DeviceEvent sameContentsNewArray = new ReadingEvent("pump-A", 72, [10, 20]);
DeviceEvent plain = new("pump-A");
DeviceEvent alarm = new AlarmEvent("pump-A", 72);

Check(reading == same && !ReferenceEquals(reading, same),
    "same runtime record type and members have value equality");
Check(reading != sameContentsNewArray,
    "array members compare by reference, not by sequence");
Check(reading != plain && reading != alarm,
    "equal base members do not erase runtime type differences");
Check(new HashSet<DeviceEvent> { reading, same, plain, alarm }.Count == 3,
    "record equality and hash codes agree in a set");

DeviceEvent moved = reading with { DeviceId = "pump-B" };
Check(moved is ReadingEvent { Value: 72 },
    "with from a base-typed variable retains the runtime derived type");
Check(!ReferenceEquals(reading, moved) && reading.DeviceId == "pump-A" &&
      moved.DeviceId == "pump-B", "with creates a new record");

ReadingEvent retuned = ((ReadingEvent)reading) with { Value = 73 };
Check(retuned.Value == 73 && ((ReadingEvent)reading).Value == 72,
    "cast to the derived type before setting derived members");
samples[0] = 99;
Check(((ReadingEvent)moved).Samples[0] == 99 && retuned.Samples[0] == 99,
    "with copies a reference to the mutable array, not its elements");
Console.WriteLine("record 继承、相等性与 with 复制验证通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

public record DeviceEvent(string DeviceId);
public record ReadingEvent(string DeviceId, int Value, int[] Samples)
    : DeviceEvent(DeviceId);
public record AlarmEvent(string DeviceId, int Severity)
    : DeviceEvent(DeviceId);
