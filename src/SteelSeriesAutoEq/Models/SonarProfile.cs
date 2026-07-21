using System.Text.Json.Serialization;

namespace SteelSeriesAutoEq.Models;

/// <summary>
/// One Sonar EQ configuration. The property names mirror the Sonar API payload so the same
/// type can be deserialized from the API and serialized into the local cache.
/// </summary>
public sealed class SonarProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Sonar channel this config belongs to. Only "game" configs are used for switching.</summary>
    [JsonPropertyName("virtualAudioDevice")]
    public string VirtualAudioDevice { get; set; } = string.Empty;

    /// <summary>Optional user-provided executable names that should map to this profile.</summary>
    [JsonPropertyName("processMatches")]
    public List<string> ProcessMatches { get; set; } = [];

    /// <summary>Lower-cased, alphanumeric-only form of the name, used for comparisons.</summary>
    [JsonIgnore]
    public string NormalizedName => TextNormalizer.Normalize(Name);
}
