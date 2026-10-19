using System.Globalization;

var commaDecimal = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
commaDecimal.NumberDecimalSeparator = ",";
commaDecimal.NumberGroupSeparator = ".";

Check(decimal.TryParse("1,234", NumberStyles.Number,
    CultureInfo.InvariantCulture, out decimal grouped) && grouped == 1234m,
    "comma as group separator");
Check(decimal.TryParse("1,234", NumberStyles.Number,
    commaDecimal, out decimal fractional) && fractional == 1.234m,
    "comma as decimal separator");

const NumberStyles protocol = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
Check(decimal.TryParse("12.50", protocol,
    CultureInfo.InvariantCulture, out decimal amount) && amount == 12.50m,
    "fixed protocol decimal point");
Check(!decimal.TryParse("1,234", protocol, CultureInfo.InvariantCulture, out _),
    "protocol rejects grouping");
Check(!decimal.TryParse(" 12.50", protocol, CultureInfo.InvariantCulture, out _),
    "protocol rejects whitespace");
Check(decimal.TryParse("-2.50", protocol,
    CultureInfo.InvariantCulture, out decimal negative) && negative < 0,
    "syntactically valid but business-invalid amount");

Check(0.1m + 0.2m == 0.3m, "decimal arithmetic for these values");
Check(Math.Round(1.005m, 2) == 1.00m, "default midpoint ToEven");
Check(Math.Round(1.005m, 2, MidpointRounding.AwayFromZero) == 1.01m,
    "explicit midpoint AwayFromZero");
Check(Math.Round(-1.005m, 2, MidpointRounding.AwayFromZero) == -1.01m,
    "negative midpoint moves away from zero");
Check(Math.Round(1.005m, 2, MidpointRounding.AwayFromZero)
    .ToString("F2", CultureInfo.InvariantCulture) == "1.01", "fixed output format");

Console.WriteLine("区域性、解析与舍入契约断言全部通过");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"断言失败：{name}");
}
