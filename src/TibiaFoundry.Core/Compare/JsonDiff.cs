using System.Text;
using Newtonsoft.Json.Linq;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Compare;

/// <summary>
/// Textual JSON difference used by the "Compare DAT files" window (port of the original
/// <c>CompareObjects</c>/<c>CompareArrays</c>; output format kept identical, including the
/// double space after "!=").
/// </summary>
public static class JsonDiff
{
    /// <summary>Lists how <paramref name="source"/> (new) differs from <paramref name="target"/> (old).</summary>
    public static string Compare(JObject source, JObject target) => CompareObjects(source, target).ToString();

    private static StringBuilder CompareObjects(JObject source, JObject target)
    {
        var sb = new StringBuilder();
        string idPrefix = "";
        foreach (var pair in source)
        {
            if (pair.Key == "id" && pair.Value?.Type == JTokenType.Integer && (long)pair.Value > 100)
                idPrefix = "id " + pair.Value + ": ";
            var value = pair.Value!;
            var other = target.GetValue(pair.Key);
            if (value.Type == JTokenType.Object)
            {
                if (other == null) sb.Append(idPrefix).Append("Key ").Append(pair.Key).Append(L.T("JsonDiff.NotFound")).Append(Environment.NewLine);
                else if (other.Type != JTokenType.Object) sb.Append(idPrefix).Append("Key ").Append(pair.Key).Append(L.T("JsonDiff.IsNotAnObjectInTarget")).Append(Environment.NewLine);
                else sb.Append(CompareObjects((JObject)value, (JObject)other));
                continue;
            }
            if (value.Type == JTokenType.Array)
            {
                if (other == null) sb.Append(idPrefix).Append("Key ").Append(pair.Key).Append(L.T("JsonDiff.NotFound")).Append(Environment.NewLine);
                else if (other is JArray otherArray) sb.Append(CompareArrays((JArray)value, otherArray, pair.Key));
                else sb.Append(idPrefix).Append("Key ").Append(pair.Key).Append(L.T("JsonDiff.IsNotAnArrayInTarget")).Append(Environment.NewLine);
                continue;
            }
            if (other == null) sb.Append(idPrefix).Append("Key ").Append(pair.Key).Append(L.T("JsonDiff.NotFound")).Append(Environment.NewLine);
            else if (!JToken.DeepEquals(value, other))
                sb.Append(idPrefix).Append("Key ").Append(pair.Key).Append(": ").Append(value).Append(" !=  ").Append(other).Append(Environment.NewLine);
        }
        return sb;
    }

    private static StringBuilder CompareArrays(JArray source, JArray target, string arrayName)
    {
        var sb = new StringBuilder();
        for (int index = 0; index < source.Count; index++)
        {
            var expected = source[index];
            if (expected.Type == JTokenType.Object)
            {
                var actual = index < target.Count && target[index] is JObject o ? o : new JObject();
                sb.Append(CompareObjects((JObject)expected, actual));
                continue;
            }
            JToken actualValue = index < target.Count ? target[index] : "";
            if (!JToken.DeepEquals(expected, actualValue))
            {
                if (string.IsNullOrEmpty(arrayName)) sb.Append("Index ").Append(index);
                else sb.Append("Key ").Append(arrayName).Append('[').Append(index).Append(']');
                sb.Append(": ").Append(expected).Append(" != ").Append(actualValue).Append(Environment.NewLine);
            }
        }
        return sb;
    }
}
