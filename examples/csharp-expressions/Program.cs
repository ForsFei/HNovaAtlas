var order = new List<string>();
int First() { order.Add("first"); return 2; }
int Second() { order.Add("second"); return 3; }
int Third() { order.Add("third"); return 4; }
Assert(First() + Second() * Third() == 14, "乘法优先于加法");
Assert(order.SequenceEqual(["first", "second", "third"]), "操作数从左到右求值");

Assert(13 / 5 / 2 == 1, "同级整数除法左结合");
Assert(7 / 2 == 3, "整数除法向零截断");
Assert((double)7 / 2 == 3.5 && (double)(7 / 2) == 3, "转换位置改变结果");

int calls = 0;
bool Check() { calls++; return true; }
bool gate = false;
_ = gate && Check();
Assert(calls == 0, "&& 短路");
_ = gate & Check();
Assert(calls == 1, "bool & 计算两侧");
bool allowed = true;
_ = allowed || Check();
Assert(calls == 1, "|| 短路");
_ = allowed | Check();
Assert(calls == 2, "bool | 计算两侧");

string? cached = null;
int builds = 0;
string Create() { builds++; return "ready"; }
cached ??= Create();
cached ??= Create();
Assert(cached == "ready" && builds == 1, "??= 仅在 null 时执行右侧");

string? absent = null;
int arguments = 0;
string Argument() { arguments++; return "x"; }
bool? contains = absent?.Contains(Argument());
Assert(contains is null && arguments == 0, "?. 跳过后续参数求值");

int branches = 0;
int Branch() { branches++; return 9; }
int selected = allowed ? 1 : Branch();
Assert(selected == 1 && branches == 0, "?: 只执行选中分支");

int n = 1;
int oldValue = n++;
int newValue = ++n;
Assert(oldValue == 1 && newValue == 3 && n == 3, "前置与后置自增");

Console.WriteLine("表达式验证通过：分组、求值顺序、短路、空值和数值边界。");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
