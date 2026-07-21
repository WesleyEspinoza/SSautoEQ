using System.Text;

namespace SteelSeriesAutoEq.Models;

/// <summary>
/// Helpers for comparing names that come from different sources (window titles, executable
/// names, profile names). Normalizing to lower-case letters and digits lets us treat
/// "Rainbow Six Siege" and "RainbowSix.exe" as comparable.
/// </summary>
public static class TextNormalizer
{
    /// <summary>Lower-cases the input and drops everything that is not a letter or digit.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    public static string StripExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }
}
