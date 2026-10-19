using System.Globalization;
using System.Text;

string rose = "\U0001F339";
Check(rose.Length == 2 && char.IsHighSurrogate(rose[0]) && char.IsLowSurrogate(rose[1]),
    "a supplementary scalar uses two UTF-16 code units");
Check(CountRunes(rose) == 1, "the rose is one Unicode scalar value");
Check(new StringInfo(rose).LengthInTextElements == 1, "the rose is one text element");
Check(char.IsHighSurrogate(rose[..1][0]), "cutting at one char leaves half a surrogate pair");

string decomposed = "e\u0301";
string composed = "\u00E9";
Check(decomposed.Length == 2 && CountRunes(decomposed) == 2,
    "base letter and combining mark are separate scalars");
var text = new StringInfo(decomposed);
Check(text.LengthInTextElements == 1 && text.SubstringByTextElements(0, 1) == decomposed,
    "the base letter and mark form one text element");
Check(!string.Equals(composed, decomposed, StringComparison.Ordinal),
    "ordinal equality does not normalize different representations");
Check(string.Equals(composed.Normalize(NormalizationForm.FormC),
    decomposed.Normalize(NormalizationForm.FormC), StringComparison.Ordinal),
    "Form C gives these canonically equivalent values the same representation");

var keys = new HashSet<string>(StringComparer.Ordinal);
Check(keys.Add(composed.Normalize(NormalizationForm.FormC)), "first normalized key is new");
Check(!keys.Add(decomposed.Normalize(NormalizationForm.FormC)), "second normalized key matches");

Console.WriteLine("UTF-16、Rune、文本元素与规范化断言全部通过。");

static int CountRunes(string value)
{
    int count = 0;
    foreach (Rune _ in value.EnumerateRunes()) count++;
    return count;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
