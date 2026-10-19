using System.Collections.Generic;

StringComparer comparer = StringComparer.OrdinalIgnoreCase;
List<string> devices = ["pump-C", "pump-A", "pump-B", "pump-A"];
string[] requested = ["PUMP-A", "pump-B", "missing"];

bool[] scanned = requested.Select(id => devices.Contains(id, comparer)).ToArray();
var index = new HashSet<string>(devices, comparer);
bool[] indexed = requested.Select(index.Contains).ToArray();
Require(scanned.SequenceEqual(indexed), "两种查询应遵循相同比较规则");
Require(index.Count == 3 && indexed.SequenceEqual([true, true, false]), "去重与查询结果");

var sorted = devices.Distinct(comparer).ToList();
sorted.Sort(comparer);
int found = sorted.BinarySearch("PUMP-B", comparer);
Require(found >= 0 && comparer.Equals(sorted[found], "pump-B"), "二分查找");

int missing = sorted.BinarySearch("pump-BB", comparer);
Require(missing < 0, "不存在的项返回负数");
sorted.Insert(~missing, "pump-BB");
Require(sorted.SequenceEqual(["pump-A", "pump-B", "pump-BB", "pump-C"], comparer),
    "按同一比较器插入后仍有序");

var reserved = new List<int>(capacity: 16);
Require(reserved.Count == 0 && reserved.Capacity >= 16, "预留空间不等于添加元素");
reserved.Add(42);
Require(reserved.Count == 1, "Add 才改变元素数");

Console.WriteLine("集合查询、二分与容量断言通过。");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
