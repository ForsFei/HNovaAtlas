using System.Linq.Expressions;
using System.Reflection;

Type ruleType = typeof(HighTemperatureRule);
RuleAttribute? marker = ruleType.GetCustomAttribute<RuleAttribute>();
Require(marker is { Name: "hot", Priority: 10 }, "读取特性元数据");
Require(ruleType.Assembly == typeof(IReadingRule).Assembly, "程序集边界");

object item = new Reading("pump-A", 72.5);
Type itemType = item.GetType();
Require(itemType == typeof(Reading), "运行时 Type");
PropertyInfo property = itemType.GetProperty(
    nameof(Reading.Value), BindingFlags.Public | BindingFlags.Instance)
    ?? throw new MissingMemberException(itemType.Name, nameof(Reading.Value));
Require(property.GetValue(item) is double value && value == 72.5, "属性反射读取");
MethodInfo method = itemType.GetMethod(nameof(Reading.IsAbove), [typeof(double)])
    ?? throw new MissingMethodException(itemType.Name, nameof(Reading.IsAbove));
Require(method.Invoke(item, [70.0]) is true, "方法反射调用");

IReadingRule rule = CreateRule("hot");
Require(rule.IsMatch(72.5) && !rule.IsMatch(68.0), "特性筛选与反射创建");
RequireThrows(() => CreateRule("missing"), "缺失规则失败");

Expression<Func<int, bool>> tree = number => number > 70;
Require(tree.Body.NodeType == ExpressionType.GreaterThan, "表达式树结构");
Require(tree.Compile()(72), "表达式树编译执行");

Console.WriteLine("反射与特性示例验证通过：元数据、成员读取、规则创建与表达式树");

static IReadingRule CreateRule(string name)
{
    Type[] candidates = typeof(IReadingRule).Assembly.GetTypes()
        .Where(type => type.IsClass && !type.IsAbstract && type.IsPublic)
        .Where(type => typeof(IReadingRule).IsAssignableFrom(type))
        .Where(type => type.GetCustomAttribute<RuleAttribute>()?.Name == name)
        .ToArray();

    if (candidates.Length != 1)
        throw new InvalidOperationException($"规则 {name} 的实现数量应为 1，实际为 {candidates.Length}");

    Type selected = candidates[0];
    if (selected.GetConstructor(Type.EmptyTypes) is null)
        throw new InvalidOperationException($"{selected.Name} 缺少公共无参构造函数");

    return (IReadingRule)(Activator.CreateInstance(selected)
        ?? throw new InvalidOperationException($"无法创建 {selected.Name}"));
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"验证失败：{label}");
}

static void RequireThrows(Action action, string label)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException($"验证失败：{label}");
}

public sealed record Reading(string DeviceId, double Value)
{
    public bool IsAbove(double threshold) => Value > threshold;
}

public interface IReadingRule { bool IsMatch(double value); }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public int Priority { get; set; }
}

[Rule("hot", Priority = 10)]
public sealed class HighTemperatureRule : IReadingRule
{
    public bool IsMatch(double value) => value >= 70;
}
