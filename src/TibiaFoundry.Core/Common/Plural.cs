namespace TibiaFoundry.Core.Common;

/// <summary>Counted nouns in messages (8.6): "1 file", "3,373 files", "2 entries" — never "file(s)".</summary>
public static class Plural
{
    public static string Count(long n, string singular, string? plural = null) => $"{n:N0} {(n == 1 ? singular : plural ?? singular + "s")}";
}
