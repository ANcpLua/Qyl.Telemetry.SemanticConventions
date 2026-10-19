// Copyright (c) Alexander Nachtmann
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Fallout.Common.IO;
using Serilog;

namespace Qyl.Telemetry.SemanticConventions.Build;

/// <summary>
///   VerifyRelease's index wait. Polls what a restore and an install read (NuGet's flat-container
///   index, npm's version document) with a backoff from 5 s to 60 s under one deadline, and records
///   the outcome in artifacts/release-&lt;version&gt;-index.status ("waiting", "indexed" or
///   "timed-out", with the elapsed seconds); release.sh reads it to tell a slow index (retry) from
///   a broken package (patch release) and refreshes the snapshot only after "indexed".
/// </summary>
internal static class ReleaseIndex
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    internal sealed record Probe(string Name, Uri Url, Func<JsonElement, bool> IsServed);

    internal static Probe NuGet(string id, string version) => new(
        $"{id} {version} (nuget.org)",
        new Uri($"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/index.json"),
        index => index.GetProperty("versions").EnumerateArray()
            .Any(listed => string.Equals(listed.GetString(), version, StringComparison.OrdinalIgnoreCase)));

    internal static Probe Npm(string name, string version) => new(
        $"{name}@{version} (npm)",
        new Uri($"https://registry.npmjs.org/{name.Replace("/", "%2f", StringComparison.Ordinal)}/{version}"),
        manifest => manifest.GetProperty("version").GetString() == version);

    internal static AbsolutePath StatusFile(AbsolutePath root, string version) =>
        root / "artifacts" / $"release-{version}-index.status";

    internal static void Wait(AbsolutePath statusFile, TimeSpan deadline, IReadOnlyList<Probe> probes)
    {
        var clock = Stopwatch.StartNew();
        var pending = probes.ToList();
        var delay = TimeSpan.FromSeconds(5);
        statusFile.Parent.CreateDirectory();
        Write(statusFile, "waiting", clock);
        while (true)
        {
            pending.RemoveAll(probe =>
            {
                if (!Serves(probe))
                    return false;
                Log.Information("indexed: {Probe} after {Seconds} s", probe.Name, (int)clock.Elapsed.TotalSeconds);
                return true;
            });
            if (pending.Count == 0)
            {
                Write(statusFile, "indexed", clock);
                return;
            }

            if (clock.Elapsed + delay > deadline)
            {
                Write(statusFile, "timed-out", clock);
                throw new TimeoutException(
                    $"not indexed after {(int)clock.Elapsed.TotalSeconds} s: {string.Join(", ", pending.Select(probe => probe.Name))}");
            }

            Thread.Sleep(delay);
            delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
        }
    }

    static bool Serves(Probe probe)
    {
        try
        {
            // NuGet's CDN can retain an old index despite Cache-Control: no-cache.
            var url = new UriBuilder(probe.Url) { Query = $"qyl-release={Guid.NewGuid():N}" }.Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = Http.Send(request);
            if (!response.IsSuccessStatusCode)
                return false;
            using var document = JsonDocument.Parse(response.Content.ReadAsStream());
            return probe.IsServed(document.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException
                                              or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    static void Write(AbsolutePath statusFile, string state, Stopwatch clock) =>
        File.WriteAllText(statusFile,
            $"{state} elapsed={(int)clock.Elapsed.TotalSeconds}s at={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
}
