using System.Numerics;

int left = 3, right = 8;
Swap(ref left, ref right);
Require(left == 8 && right == 3, "泛型交换");
Require(Greater(7, 12) == 12, "比较约束");
Require(Add(1.5m, 2.5m) == 4m, "泛型数学");

List<Dog> dogs = [new Dog("Mochi"), new Dog("Nori")];
IEnumerable<Animal> animals = dogs;
Require(animals.Count() == 2, "协变枚举");
IComparer<Animal> byName = Comparer<Animal>.Create(
    (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
IComparer<Dog> dogComparer = byName;
dogs.Sort(dogComparer);
Require(dogs[0].Name == "Mochi", "逆变比较器");

IProducer<Dog> dogSource = new DogSource();
IProducer<Animal> animalSource = dogSource;
Require(animalSource.Get().Name == "Mochi", "out T");
IConsumer<Animal> animalSink = new AnimalPrinter();
IConsumer<Dog> dogSink = animalSink;
dogSink.Accept(dogs[0]);

Reading[] readings = [new("pump-A", 72.5), new("pump-B", 68.0)];
Reading hottest = MaxBy(readings,
    Comparer<Reading>.Create((a, b) => a.Value.CompareTo(b.Value)));
Require(hottest.DeviceId == "pump-A", "泛型 API 比较策略");

Box<int>.Created++;
Box<string>.Created += 2;
Require(Box<int>.Created == 1 && Box<string>.Created == 2, "封闭泛型静态字段");
Console.WriteLine("泛型示例验证通过：约束、变型、比较器、运行时类型");

static void Swap<T>(ref T a, ref T b) => (a, b) = (b, a);
static T Greater<T>(T a, T b) where T : IComparable<T>
    => a.CompareTo(b) >= 0 ? a : b;
static T Add<T>(T a, T b) where T : INumber<T> => a + b;

static T MaxBy<T>(IEnumerable<T> source, IComparer<T> comparer)
{
    using IEnumerator<T> iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) throw new InvalidOperationException("序列为空");
    T best = iterator.Current;
    while (iterator.MoveNext())
        if (comparer.Compare(iterator.Current, best) > 0)
            best = iterator.Current;
    return best;
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"验证失败：{label}");
}

public class Animal(string name) { public string Name { get; } = name; }
public sealed class Dog(string name) : Animal(name) { }
public interface IProducer<out T> { T Get(); }
public interface IConsumer<in T> { void Accept(T value); }
public sealed class DogSource : IProducer<Dog>
{
    public Dog Get() => new("Mochi");
}
public sealed class AnimalPrinter : IConsumer<Animal>
{
    public void Accept(Animal value) => Console.WriteLine(value.Name);
}
public record Reading(string DeviceId, double Value);
public static class Box<T> { public static int Created; }
