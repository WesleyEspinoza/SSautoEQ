using System.Net.Http;
using System.Text.Json;
using SteelSeriesAutoEq.Models;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Thin wrapper over the Sonar local HTTP API. Handles the endpoints we need (list configs,
/// read the selected config, select a config) and tolerates the small differences seen in
/// Sonar's response shapes across versions.
/// </summary>
public sealed class SonarApiClient : IDisposable
{
    private readonly AppLogger _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Uri? _baseUri;

    public SonarApiClient(AppLogger logger)
    {
        _logger = logger;
        // Do not set BaseAddress — HttpClient forbids changing it after the first request.
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    public Uri? BaseUri => _baseUri;
    public bool IsConnected => _baseUri is not null;

    public void SetBaseUri(Uri baseUri) => _baseUri = baseUri;

    public void Clear() => _baseUri = null;

    public async Task<IReadOnlyList<SonarProfile>> GetGameProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync("configs", cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var profiles = new List<SonarProfile>();
        foreach (var element in EnumerateConfigElements(doc.RootElement))
        {
            var profile = ParseProfile(element);
            if (profile is null)
            {
                continue;
            }

            if (!string.Equals(profile.VirtualAudioDevice, "game", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            profiles.Add(profile);
        }

        return profiles
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SonarProfile?> GetSelectedGameProfileAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync("configs/selected", cancellationToken);
        using var doc = JsonDocument.Parse(json);

        foreach (var (channel, element) in EnumerateSelectedElements(doc.RootElement))
        {
            var profile = ParseProfile(element);
            if (profile is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.VirtualAudioDevice) &&
                !string.IsNullOrWhiteSpace(channel))
            {
                profile.VirtualAudioDevice = channel;
            }

            if (string.Equals(profile.VirtualAudioDevice, "game", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel, "game", StringComparison.OrdinalIgnoreCase))
            {
                profile.VirtualAudioDevice = "game";
                return profile;
            }
        }

        return null;
    }

    public async Task SelectProfileAsync(string configId, CancellationToken cancellationToken = default)
    {
        var path = $"configs/{configId}/select";
        _logger.Block($"Switch request:{Environment.NewLine}PUT /{path}");

        using var response = await SendAsync(HttpMethod.Put, path, cancellationToken);
        response.EnsureSuccessStatusCode();
        _logger.Block("Success");
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        if (_baseUri is null)
        {
            return false;
        }

        try
        {
            // Prefer a lightweight endpoint — full /configs can be large (hundreds of profiles).
            using var response = await SendAsync(HttpMethod.Get, "configs/selected", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GetStringAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, relativePath, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        var uri = new Uri(_baseUri!, relativePath.TrimStart('/'));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            // Caller disposes response; don't use using here.
            return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureConnected()
    {
        if (_baseUri is null)
        {
            throw new InvalidOperationException("Sonar API is not connected.");
        }
    }

    private static SonarProfile? ParseProfile(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetString(element, "id", out var id) || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        TryGetString(element, "name", out var name);
        TryGetString(element, "virtualAudioDevice", out var device);

        if (string.IsNullOrWhiteSpace(device))
        {
            TryGetString(element, "channel", out device);
        }

        return new SonarProfile
        {
            Id = id,
            Name = name ?? id,
            VirtualAudioDevice = device ?? string.Empty
        };
    }

    private static IEnumerable<JsonElement> EnumerateConfigElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("configs", out var configs) &&
                configs.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in configs.EnumerateArray())
                {
                    yield return item;
                }

                yield break;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        yield return item;
                    }
                }
                else if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    yield return property.Value;
                }
            }
        }
    }

    private static IEnumerable<(string Channel, JsonElement Element)> EnumerateSelectedElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return (string.Empty, item);
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    yield return (property.Name, property.Value);
                }
            }
        }
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = property.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    public void Dispose()
    {
        _gate.Dispose();
        _http.Dispose();
    }
}
