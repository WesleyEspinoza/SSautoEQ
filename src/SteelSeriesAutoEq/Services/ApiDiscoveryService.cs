using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Discovers the SteelSeries Sonar localhost HTTP API without hardcoded ports.
/// </summary>
public sealed class ApiDiscoveryService
{
    private static readonly int[] CommonPorts =
    [
        57864, 58000, 58001, 58002, 58003, 59000, 59001,
        6327, 6970, 6971, 18670, 18671
    ];

    private readonly AppLogger _logger;

    public ApiDiscoveryService(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<Uri?> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Discovering SteelSeries Sonar API...");

        var candidates = new List<int>();

        // 1) GG coreProps / subApps (stable entry point when available)
        foreach (var port in await DiscoverViaGgEngineAsync(cancellationToken))
        {
            AddUnique(candidates, port);
        }

        // 2) Localhost TCP connections owned by SteelSeries processes
        foreach (var port in DiscoverViaProcessConnections())
        {
            AddUnique(candidates, port);
        }

        // 3) Common localhost ports as fallback
        foreach (var port in CommonPorts)
        {
            AddUnique(candidates, port);
        }

        _logger.Info($"Probing {candidates.Count} candidate port(s)...");

        // Probe a few at a time to keep discovery responsive.
        foreach (var batch in candidates.Chunk(6))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tasks = batch.Select(async port =>
            {
                var baseUri = new Uri($"http://127.0.0.1:{port}/");
                return await IsValidSonarApiAsync(baseUri, cancellationToken)
                    ? baseUri
                    : null;
            });

            var results = await Task.WhenAll(tasks);
            var hit = results.FirstOrDefault(u => u is not null);
            if (hit is not null)
            {
                _logger.Info($"Sonar API found at {hit}");
                return hit;
            }
        }

        _logger.Warn("SteelSeries Sonar API not found. Is SteelSeries GG / Sonar running?");
        return null;
    }

    public async Task<bool> IsValidSonarApiAsync(Uri baseUri, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = CreateProbeClient();
            using var response = await http.GetAsync(new Uri(baseUri, "configs"), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IEnumerable<int>> DiscoverViaGgEngineAsync(CancellationToken cancellationToken)
    {
        var ports = new List<int>();

        foreach (var corePropsPath in GetCorePropsPaths())
        {
            if (!File.Exists(corePropsPath))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(corePropsPath, cancellationToken));
                if (!doc.RootElement.TryGetProperty("address", out var addressElement))
                {
                    continue;
                }

                var address = addressElement.GetString();
                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                var ggBase = address.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(address)
                    : new Uri($"https://{address}");

                _logger.Info($"Found GG coreProps at {corePropsPath} -> {ggBase}");

                foreach (var scheme in new[] { "https", "http" })
                {
                    try
                    {
                        var probe = new UriBuilder(ggBase) { Scheme = scheme, Path = "/subApps" }.Uri;
                        using var handler = new HttpClientHandler
                        {
                            ServerCertificateCustomValidationCallback =
                                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                        };
                        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
                        using var response = await client.GetAsync(probe, cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        ports.AddRange(ExtractSonarPorts(json));
                        break;
                    }
                    catch
                    {
                        // try next scheme
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed reading coreProps ({corePropsPath}): {ex.Message}");
            }
        }

        return ports;
    }

    private IEnumerable<int> DiscoverViaProcessConnections()
    {
        var steelSeriesPids = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (SteelSeriesHosts.ProcessNames.Any(n =>
                        process.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                {
                    steelSeriesPids.Add(process.Id);
                }
            }
            catch
            {
                // Access denied / exited — skip.
            }
            finally
            {
                process.Dispose();
            }
        }

        if (steelSeriesPids.Count == 0)
        {
            _logger.Warn("SteelSeriesGG.exe / SteelSeriesSonar.exe not detected.");
            return [];
        }

        _logger.Info($"Found SteelSeries process(es): {string.Join(", ", steelSeriesPids)}");

        try
        {
            var ports = TcpTableHelper.GetListeningPortsForPids(steelSeriesPids)
                .Where(p => p is >= 1024 and <= 65535)
                .Distinct()
                .OrderByDescending(p => p)
                .ToList();

            _logger.Info($"SteelSeries localhost listener port(s): {string.Join(", ", ports)}");
            return ports;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Unable to inspect TCP connections: {ex.Message}");
            return [];
        }
    }

    private static IEnumerable<string> GetCorePropsPaths()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Path.Combine(programData, "SteelSeries", "SteelSeries Engine 3", "coreProps.json");
        yield return Path.Combine(programData, "SteelSeries", "GG", "coreProps.json");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "SteelSeries", "GG", "coreProps.json");
    }

    private static IEnumerable<int> ExtractSonarPorts(string json)
    {
        var ports = new HashSet<int>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var port in WalkForWebServerAddress(doc.RootElement))
            {
                ports.Add(port);
            }
        }
        catch
        {
            // Fall through to regex extraction.
        }

        foreach (Match match in Regex.Matches(
                     json,
                     @"(?:webServerAddress|encryptedAddress|address)""?\s*:\s*""(?:https?://)?127\.0\.0\.1:(\d+)",
                     RegexOptions.IgnoreCase))
        {
            if (int.TryParse(match.Groups[1].Value, out var port))
            {
                ports.Add(port);
            }
        }

        return ports;
    }

    private static IEnumerable<int> WalkForWebServerAddress(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Contains("sonar", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("webServer", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("address", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            var value = property.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(value) &&
                                TryParseLocalPort(value, out var port))
                            {
                                yield return port;
                            }
                        }
                    }

                    foreach (var nested in WalkForWebServerAddress(property.Value))
                    {
                        yield return nested;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in WalkForWebServerAddress(item))
                    {
                        yield return nested;
                    }
                }
                break;
        }
    }

    private static bool TryParseLocalPort(string value, out int port)
    {
        port = 0;
        var match = Regex.Match(value, @"(?:127\.0\.0\.1|localhost):(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out port);
    }

    private static HttpClient CreateProbeClient() =>
        new() { Timeout = TimeSpan.FromMilliseconds(500) };

    private static void AddUnique(List<int> ports, int port)
    {
        if (port > 0 && !ports.Contains(port))
        {
            ports.Add(port);
        }
    }
}

/// <summary>
/// Reads listening TCP ports for specific process IDs via GetExtendedTcpTable.
/// </summary>
internal static class TcpTableHelper
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidListener = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        int tblClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    public static IEnumerable<int> GetListeningPortsForPids(HashSet<int> pids)
    {
        var bufferSize = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, AfInet, TcpTableOwnerPidListener, 0);
        var buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            var result = GetExtendedTcpTable(buffer, ref bufferSize, false, AfInet, TcpTableOwnerPidListener, 0);
            if (result != 0)
            {
                yield break;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, 4);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(rowPtr, i * rowSize));
                if (!pids.Contains((int)row.OwningPid))
                {
                    continue;
                }

                var local = new IPAddress(row.LocalAddr);
                if (!IPAddress.IsLoopback(local) && row.LocalAddr != 0)
                {
                    continue;
                }

                // Port is stored network-order in the low 16 bits.
                var port = (int)(((row.LocalPort & 0xFF) << 8) | ((row.LocalPort >> 8) & 0xFF));
                if (port > 0)
                {
                    yield return port;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
