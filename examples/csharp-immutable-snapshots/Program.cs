using System.Collections.Immutable;

List<int> source = [1, 2];
IReadOnlyList<int> view = source.AsReadOnly();
int[] arrayCopy = source.ToArray();
ImmutableArray<int> immutable = source.ToImmutableArray();

source.Add(3);
Check(view.Count == 3, "read-only view sees source changes");
Check(arrayCopy.Length == 2 && immutable.Length == 2,
    "copies retain their container contents");

ImmutableArray<int> expanded = immutable.Add(4);
Check(immutable.SequenceEqual([1, 2]), "old immutable value is unchanged");
Check(expanded.SequenceEqual([1, 2, 4]), "Add returns a new value");
Check(default(ImmutableArray<int>).IsDefault, "default is uninitialized");
Check(!ImmutableArray<int>.Empty.IsDefault && ImmutableArray<int>.Empty.IsEmpty,
    "Empty is an initialized empty collection");

var reading = new MutableReading(72);
ImmutableArray<MutableReading> shallow = ImmutableArray.Create(reading);
ImmutableArray<ReadingSnapshot> stable = [new(reading.Value)];
reading.Value = 99;
Check(shallow[0].Value == 99, "immutable container does not freeze its element");
Check(stable[0].Value == 72, "copying into immutable values forms a stable snapshot");
Console.WriteLine("只读视图、容器快照与不可变元素断言全部通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public sealed class MutableReading(int value)
{
    public int Value { get; set; } = value;
}

public readonly record struct ReadingSnapshot(int Value);
