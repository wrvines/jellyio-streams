# Jellyio Streams v2 — On-Demand Redirect Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Jellyio Streams Jellyfin plugin as an on-demand, redirect-based Stremio-like experience: search → add → play, backed by a required TRaSH-style `/data/stream` folder, working on all Jellyfin clients.

**Architecture:** `.strm` files contain a signed plugin-endpoint URL (`/AIOStreams/Stream?token=…`). At play time the plugin queries AIOStreams live, selects the best stream (or a baked quality preference), and 302-redirects (or proxies for header-requiring streams). No catalog sync; content is added one title at a time via a sidebar search page plus an optional Custom JS hook for the library view. Server-side `.strm` resolution means every Jellyfin client plays identically.

**Tech Stack:** .NET 9, Jellyfin.Controller/Model 10.11.11 (ExcludeAssets=runtime), ASP.NET Core MVC plugin, xunit for tests. Build/test with SDK at `/home/will/.dotnet` (not on PATH: prefix `export PATH="$PATH:/home/will/.dotnet" &&`).

## Global Constraints

- Target framework `net9.0`; Jellyfin packages `Jellyfin.Controller`/`Jellyfin.Model` `10.11.11` with `ExcludeAssets=runtime` (existing csproj pattern).
- The stream root is **hard-coded to `/data/stream`** (product requirement). No OutputPath setting.
- TRaSH layout inside `/data/stream`: lowercase `movies/` and `tv/` category folders.
- All pure logic (tokens, ranking, folder validation, file writing) must be testable **without the Jellyfin runtime** — pure classes use only BCL + `Microsoft.Extensions.Logging.Abstractions` types.
- Jellyfin-bound code stays thin: `OnDemandService`, `AIOStreamsController`, `Plugin`, `PluginServiceRegistrator`.
- The playback endpoint (`GET /AIOStreams/Stream`) and `GET /AIOStreams/WebUI/hook.js` are `[AllowAnonymous]`; every other endpoint keeps `[Authorize(Policy = "RequiresElevation")]`.
- The Jellyfin config endpoint serializes PascalCase and only accepts PascalCase (see manifest history) — config page JS must read/write PascalCase keys.
- Do not commit the plugin zip artifact. Commit after every task. XML doc comments on public members (existing codebase convention).
- Environment: `dotnet` is at `/home/will/.dotnet` and NOT on PATH. Every dotnet command in this plan assumes the prefix `export PATH="$PATH:/home/will/.dotnet" &&`.
- Repo root: `/code/project/jellyio-stream/jellyio-streams`. Plugin project: `Jellyfin.Plugin.AIOStreams/`. New test project: `tests/Jellyfin.Plugin.AIOStreams.Tests/`.

## File Structure

**Create:**
- `tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
- `tests/Jellyfin.Plugin.AIOStreams.Tests/PlaybackTokenServiceTests.cs`
- `tests/Jellyfin.Plugin.AIOStreams.Tests/StreamResolverTests.cs`
- `tests/Jellyfin.Plugin.AIOStreams.Tests/StreamFolderTests.cs`
- `tests/Jellyfin.Plugin.AIOStreams.Tests/OnDemandLibraryTests.cs`
- `Jellyfin.Plugin.AIOStreams/Services/PlaybackTokenService.cs`
- `Jellyfin.Plugin.AIOStreams/Services/StreamResolver.cs`
- `Jellyfin.Plugin.AIOStreams/Services/StreamFolder.cs`
- `Jellyfin.Plugin.AIOStreams/Services/OnDemandLibrary.cs`
- `Jellyfin.Plugin.AIOStreams/Services/OnDemandService.cs`
- `Jellyfin.Plugin.AIOStreams/Services/ApiStream.cs`
- `Jellyfin.Plugin.AIOStreams/Web/hook.js`

**Rewrite:**
- `Jellyfin.Plugin.AIOStreams/Configuration/PluginConfiguration.cs`
- `Jellyfin.Plugin.AIOStreams/Plugin.cs`
- `Jellyfin.Plugin.AIOStreams/Api/AIOStreamsController.cs`
- `Jellyfin.Plugin.AIOStreams/PluginServiceRegistrator.cs`
- `Jellyfin.Plugin.AIOStreams/Web/searchPage.html`
- `Jellyfin.Plugin.AIOStreams/Configuration/configPage.html`
- `Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj` (embed `hook.js`, version 2.0.0.0)
- `README.md`, `manifest.json`, `build.yaml` (version 2.0.0.0)

**Delete:**
- `Jellyfin.Plugin.AIOStreams/Services/CatalogSynchronizer.cs`
- `Jellyfin.Plugin.AIOStreams/Services/StrmLibrary.cs`
- `Jellyfin.Plugin.AIOStreams/Services/StreamModels.cs`
- `Jellyfin.Plugin.AIOStreams/Tasks/RefreshTask.cs`

**Unchanged:** `Services/AIOStreamsClient.cs`, `Services/StremioModels.cs`.

---

### Task 1: Test project scaffolding

**Files:**
- Create: `tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
- Create: `tests/Jellyfin.Plugin.AIOStreams.Tests/PlaceholderTests.cs`
- Modify: `/code/project/jellyio-stream/jellyio-streams/Jellyfin.Plugin.AIOStreams.slnx` (add test project to solution)

**Interfaces:**
- Consumes: nothing.
- Produces: a runnable `dotnet test` harness later tasks use for TDD.

- [ ] **Step 1: Create the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Jellyfin.Plugin.AIOStreams\Jellyfin.Plugin.AIOStreams.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create a placeholder test**

```csharpusing Xunit;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class PlaceholderTests
{
    [Fact]
    public void Placeholder_AlwaysPasses()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 3: Add the project to the solution**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet sln Jellyfin.Plugin.AIOStreams.slnx add tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: project added.

- [ ] **Step 4: Run the test to verify the harness works**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: 1 test PASSED. (First restore downloads xunit from nuget.org — network is available.)

- [ ] **Step 5: Commit**

```bash
git add tests/ Jellyfin.Plugin.AIOStreams.slnx
git commit -m "test: scaffold xunit test project"
```

---

### Task 2: PlaybackTokenService (HMAC tokens)

**Files:**
- Create: `Jellyfin.Plugin.AIOStreams/Services/PlaybackTokenService.cs`
- Create: `tests/Jellyfin.Plugin.AIOStreams.Tests/PlaybackTokenServiceTests.cs`

**Interfaces:**
- Consumes: nothing (pure BCL).
- Produces:
  - `public sealed record PlaybackTokenPayload(string Type, string Id, string? Quality);`
  - `public sealed class PlaybackTokenService(string secret, TimeSpan? lifetime = null)` with:
    - `public static string GenerateSecret()` → 32 random bytes, base64url, no padding.
    - `public string IssueToken(string type, string id, string? quality)` → `base64url(payloadJson) + "." + base64url(hmac)`.
    - `public bool TryVerify(string token, out PlaybackTokenPayload? payload)` → false on malformed/tampered/expired; payload null when false.
  - Token payload JSON: `{"t":type,"i":id,"q":quality-or-"auto","e":unixSeconds}` where `e` = `DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds()`.

- [ ] **Step 1: Write the failing test**

```csharpusing Xunit;

using Jellyfin.Plugin.AIOStreams.Services;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class PlaybackTokenServiceTests
{
    private static PlaybackTokenService NewService()
        => new(PlaybackTokenService.GenerateSecret(), TimeSpan.FromDays(7));

    [Fact]
    public void IssueToken_ThenVerify_RoundTrips()
    {
        var service = NewService();
        var token = service.IssueToken("movie", "tt1234567", "auto");
        Assert.True(service.TryVerify(token, out var payload));
        Assert.NotNull(payload);
        Assert.Equal("movie", payload!.Type);
        Assert.Equal("tt1234567", payload.Id);
        Assert.Equal("auto", payload.Quality);
    }

    [Fact]
    public void IssueToken_WithQuality_RoundTrips()
    {
        var service = NewService();
        var token = service.IssueToken("series", "tt123:1:2", "1080p");
        Assert.True(service.TryVerify(token, out var payload));
        Assert.Equal("1080p", payload!.Quality);
    }

    [Fact]
    public void Verify_TamperedToken_ReturnsFalse()
    {
        var service = NewService();
        var token = service.IssueToken("movie", "tt1234567", "auto");
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');
        Assert.False(service.TryVerify(tampered, out _));
    }

    [Fact]
    public void Verify_ExpiredToken_ReturnsFalse()
    {
        var service = new PlaybackTokenService(PlaybackTokenService.GenerateSecret(), TimeSpan.FromSeconds(-1));
        var token = service.IssueToken("movie", "tt1234567", "auto");
        Assert.False(service.TryVerify(token, out _));
    }

    [Fact]
    public void Verify_Garbage_ReturnsFalse()
    {
        var service = NewService();
        Assert.False(service.TryVerify("not-a-token", out _));
        Assert.False(service.TryVerify("", out _));
        Assert.False(service.TryVerify("a.b.c", out _));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var token = new PlaybackTokenService("secret-a").IssueToken("movie", "tt1", "auto");
        Assert.False(new PlaybackTokenService("secret-b").TryVerify(token, out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: FAIL — `PlaybackTokenService` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// The claims embedded in a signed playback token.
/// </summary>
public sealed record PlaybackTokenPayload(string Type, string Id, string? Quality);

/// <summary>
/// Issues and verifies HMAC-signed playback tokens used by the .strm redirect endpoint.
/// Pure BCL — no Jellyfin dependencies, fully unit-testable.
/// </summary>
public sealed class PlaybackTokenService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly byte[] _key;
    private readonly TimeSpan _lifetime;

    /// <summary>
    /// Initializes a new instance with a base64url-encoded secret (see <see cref="GenerateSecret"/>).
    /// </summary>
    public PlaybackTokenService(string secret, TimeSpan? lifetime = null)
    {
        _key = Convert.FromBase64String(secret);
        _lifetime = lifetime ?? TimeSpan.FromDays(7);
    }

    /// <summary>
    /// Generates a fresh 32-byte base64url secret for the plugin configuration.
    /// </summary>
    public static string GenerateSecret()
        => Base64Url(Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))), padding: false);

    /// <summary>
    /// Issues a token: base64url(payloadJson) + "." + base64url(HMAC-SHA256(payloadJson)).
    /// </summary>
    public string IssueToken(string type, string id, string? quality)
    {
        var payload = JsonSerializer.Serialize(new
        {
            t = type,
            i = id,
            q = string.IsNullOrWhiteSpace(quality) ? "auto" : quality,
            e = DateTimeOffset.UtcNow.Add(_lifetime).ToUnixTimeSeconds()
        }, _jsonOptions);

        var payloadB64 = Base64Url(Encoding.UTF8.GetBytes(payload), padding: false);
        var sig = Sign(payload);
        return payloadB64 + "." + Base64Url(sig, padding: false);
    }

    /// <summary>
    /// Verifies a token's signature and expiry. Returns false for malformed, tampered or expired tokens.
    /// </summary>
    public bool TryVerify(string token, out PlaybackTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        string payloadJson;
        byte[] providedSig;
        try
        {
            payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            providedSig = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSig = Sign(payloadJson);
        if (!CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var expires = root.GetProperty("e").GetInt64();
            if (DateTimeOffset.FromUnixTimeSeconds(expires) < DateTimeOffset.UtcNow)
            {
                return false;
            }

            payload = new PlaybackTokenPayload(
                root.GetProperty("t").GetString() ?? string.Empty,
                root.GetProperty("i").GetString() ?? string.Empty,
                root.TryGetProperty("q", out var q) ? q.GetString() : null);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private byte[] Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string Base64Url(byte[] bytes, bool padding)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var b64 = value.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        return Convert.FromBase64String(b64);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: all 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Services/PlaybackTokenService.cs tests/Jellyfin.Plugin.AIOStreams.Tests/PlaybackTokenServiceTests.cs
git commit -m "feat: HMAC playback token service"
```

---

### Task 3: StreamResolver (quality ranking & selection)

**Files:**
- Create: `Jellyfin.Plugin.AIOStreams/Services/StreamResolver.cs`
- Create: `tests/Jellyfin.Plugin.AIOStreams.Tests/StreamResolverTests.cs`

**Interfaces:**
- Consumes: `StreamResult`/`StreamBehaviorHints` from existing `Services/StremioModels.cs`.
- Produces:
  - `public static class StreamResolver` with:
    - `public static IReadOnlyList<StreamResult> Rank(IEnumerable<StreamResult> streams)` — best first.
    - `public static StreamResult? Select(IEnumerable<StreamResult> streams, string? quality)` — `quality` null/"auto" → `Rank()[0]`; `"2160p"`/`"1080p"`/`"720p"` → first ranked stream whose quality family matches, else `Rank()[0]`.
    - `public static string? ResolveQuality(string? text)` — returns `"2160p"`, `"1080p"`, `"720p"`, `"480p"` or null. Maps `4k`/`uhd`→`2160p`, `8k`→`4320p`.
  - Ranking rules (highest score first, stable): resolution (`4320p`=4320, `2160p`=2160, `1080p`=1080, `720p`=720, `480p`=480, else 0) ×1000; HDR bonus: contains `DV`/`Dolby Vision` +200, `HDR10+` +150, `HDR` +100 (SDR 0); size bonus: `BehaviorHints.VideoSize` bytes → `min(log2(bytes), 45)` (bigger preferred within same resolution). Ties keep input order.

- [ ] **Step 1: Write the failing test**

```csharpusing Xunit;

using Jellyfin.Plugin.AIOStreams.Services;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class StreamResolverTests
{
    private static StreamResult Stream(string title, long? size = null)
        => new()
        {
            Title = title,
            BehaviorHints = size is null ? null : new StreamBehaviorHints { VideoSize = size }
        };

    [Theory]
    [InlineData("Dune 2160p HDR10+ WEB-DL", "2160p")]
    [InlineData("Dune 1080p", "1080p")]
    [InlineData("Dune 4k UHD REMUX", "2160p")]
    [InlineData("Dune 8K DV", "4320p")]
    [InlineData("Dune", null)]
    public void ResolveQuality_DetectsResolution(string text, string? expected)
    {
        Assert.Equal(expected, StreamResolver.ResolveQuality(text));
    }

    [Fact]
    public void Rank_PrefersHigherResolution()
    {
        var ranked = StreamResolver.Rank(new[]
        {
            Stream("Dune 1080p"),
            Stream("Dune 2160p HDR"),
            Stream("Dune 720p")
        });
        Assert.Equal("Dune 2160p HDR", ranked[0].Title);
        Assert.Equal("Dune 1080p", ranked[1].Title);
        Assert.Equal("Dune 720p", ranked[2].Title);
    }

    [Fact]
    public void Rank_PrefersHdrOverSdr_AtSameResolution()
    {
        var ranked = StreamResolver.Rank(new[]
        {
            Stream("Dune 2160p SDR"),
            Stream("Dune 2160p DV")
        });
        Assert.Equal("Dune 2160p DV", ranked[0].Title);
    }

    [Fact]
    public void Rank_PrefersLargerFile_AtSameResolutionAndHdr()
    {
        var ranked = StreamResolver.Rank(new[]
        {
            Stream("Dune 1080p", size: 2L * 1024 * 1024 * 1024),
            Stream("Dune 1080p", size: 5L * 1024 * 1024 * 1024)
        });
        Assert.Equal(5L * 1024 * 1024 * 1024, ranked[0].BehaviorHints!.VideoSize);
    }

    [Fact]
    public void Select_WithAuto_PicksBest()
    {
        var streams = new[]
        {
            Stream("Dune 1080p"),
            Stream("Dune 2160p HDR")
        };
        Assert.Equal("Dune 2160p HDR", StreamResolver.Select(streams, null)!.Title);
        Assert.Equal("Dune 2160p HDR", StreamResolver.Select(streams, "auto")!.Title);
    }

    [Fact]
    public void Select_WithQuality_MatchingStreamFirst()
    {
        var streams = new[]
        {
            Stream("Dune 2160p HDR"),
            Stream("Dune 1080p WEB-DL")
        };
        Assert.Equal("Dune 1080p WEB-DL", StreamResolver.Select(streams, "1080p")!.Title);
    }

    [Fact]
    public void Select_WithQualityNoMatch_FallsBackToBest()
    {
        var streams = new[]
        {
            Stream("Dune 1080p"),
            Stream("Dune 720p")
        };
        Assert.Equal("Dune 1080p", StreamResolver.Select(streams, "2160p")!.Title);
    }

    [Fact]
    public void Select_Empty_ReturnsNull()
    {
        Assert.Null(StreamResolver.Select(Array.Empty<StreamResult>(), null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: FAIL — `StreamResolver` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// Ranks and selects AIOStreams streams by quality (Stremio-style "pick the best").
/// Pure BCL — fully unit-testable.
/// </summary>
public static partial class StreamResolver
{
    [GeneratedRegex(@"\b(4320|2160|1080|720|480|360)p\b|\b(4k|uhd)\b|\b8k\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityRegex();

    /// <summary>
    /// Returns the resolution family ("2160p", "1080p", ...) found in the text, or null.
    /// </summary>
    public static string? ResolveQuality(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = QualityRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Success ? match.Groups[1].Value : match.Value;
        return value.ToLowerInvariant() switch
        {
            "4k" or "uhd" => "2160p",
            "8k" => "4320p",
            _ => value + "p"
        };
    }

    /// <summary>
    /// Ranks streams best-first by resolution, then HDR, then file size. Stable.
    /// </summary>
    public static IReadOnlyList<StreamResult> Rank(IEnumerable<StreamResult> streams)
    {
        var result = new List<StreamResult>(streams);
        var scores = new Dictionary<StreamResult, long>();
        foreach (var stream in result)
        {
            scores[stream] = Score(stream);
        }

        result.Sort((a, b) => scores[b].CompareTo(scores[a]));
        return result;
    }

    /// <summary>
    /// Selects the stream to play. "auto"/null picks the top-ranked stream;
    /// a quality family ("1080p") picks the best stream of that family, falling
    /// back to the top-ranked stream when none matches.
    /// </summary>
    public static StreamResult? Select(IEnumerable<StreamResult> streams, string? quality)
    {
        var ranked = Rank(streams);
        if (ranked.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(quality) || quality.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return ranked[0];
        }

        var family = quality.StartsWith("43", StringComparison.OrdinalIgnoreCase) ? "4320p"
            : quality.StartsWith("21", StringComparison.OrdinalIgnoreCase) ? "2160p"
            : quality.StartsWith("108", StringComparison.OrdinalIgnoreCase) ? "1080p"
            : quality.StartsWith("72", StringComparison.OrdinalIgnoreCase) ? "720p"
            : null;

        if (family is not null)
        {
            var match = ranked.FirstOrDefault(s =>
                string.Equals(ResolveQuality((s.Title ?? s.Name) + " " + (s.Description ?? string.Empty)), family, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return ranked[0];
    }

    private static long Score(StreamResult stream)
    {
        var label = (stream.Title ?? string.Empty) + " " + (stream.Name ?? string.Empty) + " " + (stream.Description ?? string.Empty);
        var quality = ResolveQuality(label);
        var resolution = quality switch
        {
            "4320p" => 4320,
            "2160p" => 2160,
            "1080p" => 1080,
            "720p" => 720,
            "480p" => 480,
            "360p" => 360,
            _ => 0
        };

        long score = resolution * 1000L;
        var upper = label.ToUpperInvariant();
        if (upper.Contains("DOLBY VISION") || upper.Contains("DV"))
        {
            score += 200;
        }
        else if (upper.Contains("HDR10+"))
        {
            score += 150;
        }
        else if (upper.Contains("HDR"))
        {
            score += 100;
        }

        var size = stream.BehaviorHints?.VideoSize ?? 0;
        if (size > 0)
        {
            score += Math.Min(45, (long)Math.Log2(size));
        }

        return score;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Services/StreamResolver.cs tests/Jellyfin.Plugin.AIOStreams.Tests/StreamResolverTests.cs
git commit -m "feat: stream quality ranking and selection"
```

---

### Task 4: StreamFolder (required /data/stream validation + TRaSH paths)

**Files:**
- Create: `Jellyfin.Plugin.AIOStreams/Services/StreamFolder.cs`
- Create: `tests/Jellyfin.Plugin.AIOStreams.Tests/StreamFolderTests.cs`

**Interfaces:**
- Consumes: nothing (pure BCL).
- Produces:
  - `public enum FolderState { Ok, Missing, NotDirectory, NotWritable }`
  - `public static class StreamFolder` with:
    - `public const string MoviesDirName = "movies";`
    - `public const string TvDirName = "tv";`
    - `public static FolderState Validate(string root)` — missing → Missing; exists-but-file → NotDirectory; probe write+delete `root/.jellyio-probe` → NotWritable on `IOException`/`UnauthorizedAccessException`; else Ok.
    - `public static void Create(string root)` — creates root, `movies/`, `tv/` (`Directory.CreateDirectory`).
    - `public static string BuildFolderName(string title, string? year)` — `"Dune"` or `"Dune (2021)"` (sanitized, max 120 chars).
    - `public static string SanitizeTitle(string value)` — removes invalid filename chars, collapses whitespace, trims dots/spaces, max 120.
    - `public static string MovieDir(string root, string title, string? year)` → `Path.Combine(root, "movies", BuildFolderName(title, year))`.
    - `public static string TvShowDir(string root, string title, string? year)` → `Path.Combine(root, "tv", BuildFolderName(title, year))`.
    - `public static string EpisodeFileName(int season, int episode)` → `"S01E02.strm"`.
    - `public static string? ExtractYear(string? releaseInfo)` — first `(19|20)\d{2}` in value.
    - `public static string? ExtractImdbId(string? id)` — `tt\d{6,10}` in value.

- [ ] **Step 1: Write the failing test**

```csharpusing Xunit;

using Jellyfin.Plugin.AIOStreams.Services;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class StreamFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyio-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Validate_Missing_ReturnsMissing()
    {
        Assert.Equal(FolderState.Missing, StreamFolder.Validate(_root));
    }

    [Fact]
    public void Validate_File_ReturnsNotDirectory()
    {
        File.WriteAllText(_root, "x");
        Assert.Equal(FolderState.NotDirectory, StreamFolder.Validate(_root));
    }

    [Fact]
    public void Validate_EmptyDirectory_ReturnsOk()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(FolderState.Ok, StreamFolder.Validate(_root));
    }

    [Fact]
    public void Create_CreatesRootAndCategories()
    {
        StreamFolder.Create(_root);
        Assert.True(Directory.Exists(Path.Combine(_root, StreamFolder.MoviesDirName)));
        Assert.True(Directory.Exists(Path.Combine(_root, StreamFolder.TvDirName)));
        Assert.Equal(FolderState.Ok, StreamFolder.Validate(_root));
    }

    [Theory]
    [InlineData("Dune", null, "Dune")]
    [InlineData("Dune", "2021", "Dune (2021)")]
    [InlineData("  My  :  Title?  ", null, "My Title")]
    public void BuildFolderName_FormatsCorrectly(string title, string? year, string expected)
    {
        Assert.Equal(expected, StreamFolder.BuildFolderName(title, year));
    }

    [Fact]
    public void MovieDir_UsesTrashLayout()
    {
        var dir = StreamFolder.MovieDir(_root, "Dune", "2021");
        Assert.Equal(Path.Combine(_root, "movies", "Dune (2021)"), dir);
    }

    [Fact]
    public void TvShowDir_UsesTrashLayout()
    {
        var dir = StreamFolder.TvShowDir(_root, "Dune", "2021");
        Assert.Equal(Path.Combine(_root, "tv", "Dune (2021)"), dir);
    }

    [Theory]
    [InlineData(1, 2, "S01E02.strm")]
    [InlineData(10, 5, "S10E05.strm")]
    public void EpisodeFileName_FormatsCorrectly(int season, int episode, string expected)
    {
        Assert.Equal(expected, StreamFolder.EpisodeFileName(season, episode));
    }

    [Theory]
    [InlineData("2021", "2021")]
    [InlineData("2021-05-15", "2021")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractYear_Parses(string? input, string? expected)
    {
        Assert.Equal(expected, StreamFolder.ExtractYear(input));
    }

    [Theory]
    [InlineData("tt1234567", "tt1234567")]
    [InlineData("kitsu:123", null)]
    [InlineData(null, null)]
    public void ExtractImdbId_Parses(string? input, string? expected)
    {
        Assert.Equal(expected, StreamFolder.ExtractImdbId(input));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: FAIL — `StreamFolder` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// Result of validating the required /data/stream folder.
/// </summary>
public enum FolderState
{
    Ok,
    Missing,
    NotDirectory,
    NotWritable
}

/// <summary>
/// Validation, creation and TRaSH-style path building for the required stream folder.
/// Pure BCL — fully unit-testable.
/// </summary>
public static partial class StreamFolder
{
    /// <summary>
    /// Gets the TRaSH "category" folder for movies.
    /// </summary>
    public const string MoviesDirName = "movies";

    /// <summary>
    /// Gets the TRaSH "category" folder for TV shows.
    /// </summary>
    public const string TvDirName = "tv";

    private const string ProbeFileName = ".jellyio-probe";

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\btt\d{6,10}\b")]
    private static partial Regex ImdbRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Validates that <paramref name="root"/> exists, is a directory, and is writable.
    /// </summary>
    public static FolderState Validate(string root)
    {
        if (!Directory.Exists(root))
        {
            return File.Exists(root) ? FolderState.NotDirectory : FolderState.Missing;
        }

        if (File.Exists(root))
        {
            return FolderState.NotDirectory;
        }

        var probe = Path.Combine(root, ProbeFileName);
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return FolderState.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FolderState.NotWritable;
        }
    }

    /// <summary>
    /// Creates the root and the TRaSH category subfolders. Idempotent.
    /// </summary>
    public static void Create(string root)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, MoviesDirName));
        Directory.CreateDirectory(Path.Combine(root, TvDirName));
    }

    /// <summary>
    /// Builds a folder name like "Dune (2021)" from a title and year.
    /// </summary>
    public static string BuildFolderName(string title, string? year)
    {
        var name = SanitizeTitle(title);
        return string.IsNullOrWhiteSpace(year) ? name : $"{name} ({year})";
    }

    /// <summary>
    /// Cleans a title for safe use as a folder name.
    /// </summary>
    public static string SanitizeTitle(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? ' ' : c);
        }

        var result = WhitespaceRegex().Replace(sb.ToString(), " ").Trim().Trim('.', ' ');
        return result.Length > 120 ? result[..120] : result;
    }

    /// <summary>
    /// Gets the movie folder path for a title under the root.
    /// </summary>
    public static string MovieDir(string root, string title, string? year)
        => Path.Combine(root, MoviesDirName, BuildFolderName(title, year));

    /// <summary>
    /// Gets the TV show folder path for a title under the root.
    /// </summary>
    public static string TvShowDir(string root, string title, string? year)
        => Path.Combine(root, TvDirName, BuildFolderName(title, year));

    /// <summary>
    /// Builds a Jellyfin episode file name like "S01E02.strm".
    /// </summary>
    public static string EpisodeFileName(int season, int episode)
        => $"S{season:00}E{episode:00}.strm";

    /// <summary>
    /// Extracts a 4-digit year from a Stremio releaseInfo value, or null.
    /// </summary>
    public static string? ExtractYear(string? releaseInfo)
    {
        if (string.IsNullOrWhiteSpace(releaseInfo))
        {
            return null;
        }

        var match = YearRegex().Match(releaseInfo);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Extracts an IMDb id ("tt1234567") from a Stremio item id, or null.
    /// </summary>
    public static string? ExtractImdbId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var match = ImdbRegex().Match(id);
        return match.Success ? match.Value : null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Services/StreamFolder.cs tests/Jellyfin.Plugin.AIOStreams.Tests/StreamFolderTests.cs
git commit -m "feat: /data/stream validation and TRaSH path building"
```

---

### Task 5: OnDemandLibrary (.strm + nfo writer, list, remove)

**Files:**
- Create: `Jellyfin.Plugin.AIOStreams/Services/OnDemandLibrary.cs`
- Create: `tests/Jellyfin.Plugin.AIOStreams.Tests/OnDemandLibraryTests.cs`
- Delete: `Jellyfin.Plugin.AIOStreams/Services/StreamModels.cs`

**Interfaces:**
- Consumes: `StreamFolder` (Task 4) for paths/names.
- Produces:
  - `public sealed record EpisodeEntry(int Season, int Episode, string PlaybackUrl);`
  - `public sealed record WrittenFiles(int Strms, int Files);`
  - `public sealed record TitleOnDisk(string Name, string? Year, string Type);` — Type is `"movie"` or `"series"`.
  - `public static class OnDemandLibrary` with:
    - `public static Task<WrittenFiles> WriteMovieAsync(string root, string title, string? year, string? imdbId, string playbackUrl, CancellationToken ct)` — writes `<MovieDir>/<FolderName>.strm` containing the URL text, plus `movie.nfo`; returns counts.
    - `public static Task<WrittenFiles> WriteShowAsync(string root, string title, string? year, string? imdbId, IReadOnlyList<EpisodeEntry> episodes, CancellationToken ct)` — writes `tvshow.nfo` plus one `.strm` per episode in `Season NN/` folders; returns counts.
    - `public static bool RemoveTitle(string root, string type, string title, string? year)` — deletes the `movies/` or `tv/` folder (recursive), returns whether anything was deleted.
    - `public static IReadOnlyList<TitleOnDisk> List(string root)` — movies from `movies/`, series from `tv/`; parses `Name (Year)` folder names via regex `^(.*?)(?:\s*\((\d{4})\))?$`.
  - nfo content (same XML as v1, unchanged in shape):
    - movie: `<movie><title>…</title><year>…</year><uniqueid type="imdb">…</uniqueid></movie>`
    - tvshow: `<tvshow>…</tvshow>`
  - XML escaping: `& < > " '`.

- [ ] **Step 1: Write the failing test**

```csharpusing Xunit;

using Jellyfin.Plugin.AIOStreams.Services;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class OnDemandLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyio-tests-" + Guid.NewGuid().ToString("N"));

    public OnDemandLibraryTests()
    {
        StreamFolder.Create(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteMovieAsync_WritesStrmAndNfo()
    {
        var result = await OnDemandLibrary.WriteMovieAsync(_root, "Dune", "2021", "tt1160419", "https://media.example/play?x=1", CancellationToken.None);

        var strmPath = Path.Combine(_root, "movies", "Dune (2021)", "Dune (2021).strm");
        Assert.True(File.Exists(strmPath));
        Assert.Equal("https://media.example/play?x=1", await File.ReadAllTextAsync(strmPath));

        var nfoPath = Path.Combine(_root, "movies", "Dune (2021)", "movie.nfo");
        var nfo = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<uniqueid type=\"imdb\">tt1160419</uniqueid>", nfo);

        Assert.Equal(1, result.Strms);
        Assert.Equal(2, result.Files);
    }

    [Fact]
    public async Task WriteShowAsync_WritesEpisodesPerSeason()
    {
        var result = await OnDemandLibrary.WriteShowAsync(_root, "Dune", "2021", "tt1160419", new[]
        {
            new EpisodeEntry(1, 1, "https://media.example/e1"),
            new EpisodeEntry(1, 2, "https://media.example/e2"),
            new EpisodeEntry(2, 1, "https://media.example/e3")
        }, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "tv", "Dune (2021)", "tvshow.nfo")));
        Assert.Equal("https://media.example/e1", await File.ReadAllTextAsync(Path.Combine(_root, "tv", "Dune (2021)", "Season 01", "S01E01.strm")));
        Assert.True(File.Exists(Path.Combine(_root, "tv", "Dune (2021)", "Season 02", "S02E01.strm")));
        Assert.Equal(3, result.Strms);
        Assert.Equal(4, result.Files);
    }

    [Fact]
    public async Task RemoveTitle_DeletesFolder()
    {
        await OnDemandLibrary.WriteMovieAsync(_root, "Dune", "2021", "tt1160419", "https://media.example/play", CancellationToken.None);
        var removed = OnDemandLibrary.RemoveTitle(_root, "movie", "Dune", "2021");
        Assert.True(removed);
        Assert.False(Directory.Exists(Path.Combine(_root, "movies", "Dune (2021)")));

        var removedAgain = OnDemandLibrary.RemoveTitle(_root, "movie", "Dune", "2021");
        Assert.False(removedAgain);
    }

    [Fact]
    public async Task List_ListsMoviesAndSeries()
    {
        await OnDemandLibrary.WriteMovieAsync(_root, "Dune", "2021", "tt1160419", "https://media.example/play", CancellationToken.None);
        await OnDemandLibrary.WriteShowAsync(_root, "Severance", "2022", "tt11280740", new[] { new EpisodeEntry(1, 1, "https://media.example/e1") }, CancellationToken.None);

        var titles = OnDemandLibrary.List(_root);
        Assert.Equal(2, titles.Count);
        var movie = titles.Single(t => t.Type == "movie");
        var series = titles.Single(t => t.Type == "series");
        Assert.Equal("Dune", movie.Name);
        Assert.Equal("2021", movie.Year);
        Assert.Equal("Severance", series.Name);
        Assert.Equal("2022", series.Year);
    }

    [Fact]
    public void List_WithoutYear_StillParses()
    {
        Directory.CreateDirectory(Path.Combine(_root, "movies", "Untitled"));
        var titles = OnDemandLibrary.List(_root);
        Assert.Equal("Untitled", Assert.Single(titles).Name);
        Assert.Null(Assert.Single(titles).Year);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: FAIL — `OnDemandLibrary` does not exist.

- [ ] **Step 3: Write the implementation and delete StreamModels.cs**

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// One episode of a series to write as a single .strm file.
/// </summary>
public sealed record EpisodeEntry(int Season, int Episode, string PlaybackUrl);

/// <summary>
/// Counts of files written by a library operation.
/// </summary>
public sealed record WrittenFiles(int Strms, int Files);

/// <summary>
/// A title currently on disk in the managed stream folder.
/// </summary>
public sealed record TitleOnDisk(string Name, string? Year, string Type);

/// <summary>
/// Writes .strm files (pointing at the plugin playback endpoint) plus nfo sidecars
/// into the required /data/stream folder using the TRaSH layout. Pure BCL — fully unit-testable.
/// </summary>
public static partial class OnDemandLibrary
{
    [GeneratedRegex(@"^(.*?)(?:\s*\((\d{4})\))?\s*$")]
    private static partial Regex TitleFolderRegex();

    /// <summary>
    /// Writes the movie .strm (containing <paramref name="playbackUrl"/>) plus movie.nfo.
    /// </summary>
    public static async Task<WrittenFiles> WriteMovieAsync(
        string root,
        string title,
        string? year,
        string? imdbId,
        string playbackUrl,
        CancellationToken cancellationToken)
    {
        var folderName = StreamFolder.BuildFolderName(title, year);
        var dir = StreamFolder.MovieDir(root, title, year);
        Directory.CreateDirectory(dir);

        var strmPath = Path.Combine(dir, folderName + ".strm");
        await File.WriteAllTextAsync(strmPath, playbackUrl, cancellationToken).ConfigureAwait(false);

        var nfoPath = Path.Combine(dir, "movie.nfo");
        await File.WriteAllTextAsync(nfoPath, BuildNfo("movie", title, year, imdbId), cancellationToken).ConfigureAwait(false);

        return new WrittenFiles(Strms: 1, Files: 2);
    }

    /// <summary>
    /// Writes tvshow.nfo plus one .strm per episode (S01E01.strm under "Season 01").
    /// </summary>
    public static async Task<WrittenFiles> WriteShowAsync(
        string root,
        string title,
        string? year,
        string? imdbId,
        IReadOnlyList<EpisodeEntry> episodes,
        CancellationToken cancellationToken)
    {
        var showDir = StreamFolder.TvShowDir(root, title, year);
        Directory.CreateDirectory(showDir);

        var nfoPath = Path.Combine(showDir, "tvshow.nfo");
        await File.WriteAllTextAsync(nfoPath, BuildNfo("tvshow", title, year, imdbId), cancellationToken).ConfigureAwait(false);

        var strms = 0;
        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seasonDir = Path.Combine(showDir, $"Season {episode.Season:00}");
            Directory.CreateDirectory(seasonDir);

            var strmPath = Path.Combine(seasonDir, StreamFolder.EpisodeFileName(episode.Season, episode.Episode));
            await File.WriteAllTextAsync(strmPath, episode.PlaybackUrl, cancellationToken).ConfigureAwait(false);
            strms++;
        }

        return new WrittenFiles(Strms: strms, Files: strms + 1);
    }

    /// <summary>
    /// Deletes the folder for a movie or series title. Returns true when something was deleted.
    /// </summary>
    public static bool RemoveTitle(string root, string type, string title, string? year)
    {
        var dir = string.Equals(type, "series", StringComparison.OrdinalIgnoreCase)
            ? StreamFolder.TvShowDir(root, title, year)
            : StreamFolder.MovieDir(root, title, year);

        if (!Directory.Exists(dir))
        {
            return false;
        }

        Directory.Delete(dir, recursive: true);
        return true;
    }

    /// <summary>
    /// Lists the titles currently on disk, parsing "Name (Year)" folder names.
    /// </summary>
    public static IReadOnlyList<TitleOnDisk> List(string root)
    {
        var result = new List<TitleOnDisk>();
        result.AddRange(ListCategory(Path.Combine(root, StreamFolder.MoviesDirName), "movie"));
        result.AddRange(ListCategory(Path.Combine(root, StreamFolder.TvDirName), "series"));
        return result;
    }

    private static IReadOnlyList<TitleOnDisk> ListCategory(string dir, string type)
    {
        var result = new List<TitleOnDisk>();
        if (!Directory.Exists(dir))
        {
            return result;
        }

        foreach (var folder in Directory.EnumerateDirectories(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(folder);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var match = TitleFolderRegex().Match(folderName);
            if (!match.Success)
            {
                continue;
            }

            result.Add(new TitleOnDisk(
                match.Groups[1].Value.Trim(),
                match.Groups[2].Success ? match.Groups[2].Value : null,
                type));
        }

        return result;
    }

    private static string BuildNfo(string rootElement, string title, string? year, string? imdbId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine($"<{rootElement}>");
        sb.AppendLine($"  <title>{EscapeXml(title)}</title>");
        if (!string.IsNullOrWhiteSpace(year))
        {
            sb.AppendLine($"  <year>{EscapeXml(year)}</year>");
        }

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            sb.AppendLine($"  <uniqueid type=\"imdb\">{EscapeXml(imdbId)}</uniqueid>");
        }

        sb.AppendLine($"</{rootElement}>");
        return sb.ToString();
    }

    private static string EscapeXml(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
}
```

Then delete the obsolete file:

```bash
git rm Jellyfin.Plugin.AIOStreams/Services/StreamModels.cs
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: all tests PASS (placeholder test from Task 1 still included).

- [ ] **Step 5: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Services/OnDemandLibrary.cs tests/Jellyfin.Plugin.AIOStreams.Tests/OnDemandLibraryTests.cs
git commit -m "feat: on-demand strm/nfo writer with TRaSH layout; drop old stream models"
```

---

### Task 6: Configuration v2 + Plugin.cs (/data/stream root)

**Files:**
- Rewrite: `Jellyfin.Plugin.AIOStreams/Configuration/PluginConfiguration.cs`
- Rewrite: `Jellyfin.Plugin.AIOStreams/Plugin.cs`

**Interfaces:**
- Consumes: `PlaybackTokenService.GenerateSecret()` (Task 2), `StreamFolder` (Task 4).
- Produces:
  - `PluginConfiguration` fields (PascalCase, per Jellyfin config serializer):
    - `string AddonUrl` (default `""`)
    - `string ExtraQuery` (default `""`)
    - `bool AutoCreateStreamFolder` (default `true`)
    - `bool QualityPickerAtAdd` (default `false`)
    - `string DefaultQuality` (default `"auto"`)
    - `int MaxStreamsShown` (default `10`)
    - `string PlaybackSecret` (default `""`)
  - `Plugin`:
    - `public static string StreamRoot => "/data/stream";` (hard requirement; replaces `DefaultOutputPath`/`ResolvedOutputPath`).
    - `public void EnsurePlaybackSecret()` — if `Configuration.PlaybackSecret` empty → `GenerateSecret()`, save.
    - `public string EnsureStreamFolder()` — returns `FolderState.ToString()`; if `AutoCreateStreamFolder` and state is `Missing` → `StreamFolder.Create(StreamRoot)` then revalidate; if `NotDirectory`/`NotWritable` leave as-is. Called by controller endpoints before search/add.
    - `GetPages()` unchanged: config page + `JellyioStreamsSearch` main-menu page.
    - Keeps `public static Plugin? Instance`.

- [ ] **Step 1: Rewrite PluginConfiguration.cs**

```csharp
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AIOStreams.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the AIOStreams install URL.
    /// </summary>
    public string AddonUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional query parameters appended to every addon request.
    /// </summary>
    public string ExtraQuery { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the plugin may create /data/stream itself when missing.
    /// </summary>
    public bool AutoCreateStreamFolder { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the search UI shows a quality picker when adding.
    /// </summary>
    public bool QualityPickerAtAdd { get; set; }

    /// <summary>
    /// Gets or sets the preferred quality when the picker is off ("auto", "2160p", "1080p", "720p").
    /// </summary>
    public string DefaultQuality { get; set; } = "auto";

    /// <summary>
    /// Gets or sets the maximum number of streams shown in the quality picker.
    /// </summary>
    public int MaxStreamsShown { get; set; } = 10;

    /// <summary>
    /// Gets or sets the HMAC secret used to sign playback tokens. Generated automatically; never displayed.
    /// </summary>
    public string PlaybackSecret { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Rewrite Plugin.cs**

```csharp
using Jellyfin.Plugin.AIOStreams.Configuration;
using Jellyfin.Plugin.AIOStreams.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AIOStreams;

/// <summary>
/// AIOStreams plugin: a Stremio-like on-demand experience inside Jellyfin,
/// backed by a required /data/stream folder of .strm files.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly Guid _id = new("3a9f7c1e-5b6d-4e8f-9c2a-1d4b5e6f7a8b");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    public override Guid Id => _id;

    public override string Name => "Jellyio Streams";

    public override string Description => "Stream Stremio-addon content from your self-hosted AIOStreams instance.";

    /// <summary>
    /// Gets the required TRaSH-style folder that holds the generated .strm library.
    /// </summary>
    public static string StreamRoot => "/data/stream";

    /// <summary>
    /// Ensures a playback HMAC secret exists, generating and saving one when missing.
    /// </summary>
    public void EnsurePlaybackSecret()
    {
        if (!string.IsNullOrEmpty(Configuration.PlaybackSecret))
        {
            return;
        }

        Configuration.PlaybackSecret = PlaybackTokenService.GenerateSecret();
        SaveConfiguration();
    }

    /// <summary>
    /// Ensures the stream folder exists when auto-create is enabled. Returns the current folder state.
    /// </summary>
    public string EnsureStreamFolder()
    {
        var state = StreamFolder.Validate(StreamRoot);
        if (state == FolderState.Missing && Configuration.AutoCreateStreamFolder)
        {
            StreamFolder.Create(StreamRoot);
            state = StreamFolder.Validate(StreamRoot);
        }

        return state.ToString();
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "Jellyio Streams",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "JellyioStreamsSearch",
                DisplayName = "Jellyio Streams",
                EmbeddedResourcePath = GetType().Namespace + ".Web.searchPage.html",
                EnableInMainMenu = true
            }
        };
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet build Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release`
Expected: BUILD SUCCEEDED. (The old `SyncResult`/`CatalogSynchronizer` references in the controller will now fail — Task 8 rewrites the controller; if the build fails only on controller references, that is expected and acceptable; verify the two rewritten files compile by checking error list only mentions the controller/registrator.)

- [ ] **Step 4: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Configuration/PluginConfiguration.cs Jellyfin.Plugin.AIOStreams/Plugin.cs
git commit -m "feat: v2 configuration and required /data/stream root"
```

---

### Task 7: OnDemandService (orchestration: add / remove / search / resolve)

**Files:**
- Create: `Jellyfin.Plugin.AIOStreams/Services/OnDemandService.cs`
- Create: `Jellyfin.Plugin.AIOStreams/Services/ApiStream.cs`

**Interfaces:**
- Consumes: `AIOStreamsClient` (existing), `PlaybackTokenService` (Task 2), `StreamResolver` (Task 3), `StreamFolder` (Task 4), `OnDemandLibrary` (Task 5), `Plugin.Instance` (Task 6).
- Produces:
  - `public sealed class TitleAddRequest { public string Type = "movie"; public string Id = ""; public string? Name; public string? ReleaseInfo; public string? Quality; }`
  - `public sealed record AddResult(int Movies, int Shows, int Episodes, int Streams, int Files);`
  - `public sealed class OnDemandService(AIOStreamsClient client, ILibraryMonitor libraryMonitor, ILogger<OnDemandService> logger)` with:
    - `public Task<AddResult> AddTitleAsync(TitleAddRequest request, string playbackBaseUrl, CancellationToken ct)`
    - `public Task<bool> RemoveTitleAsync(string type, string title, string? year, CancellationToken ct)`
    - `public Task<IReadOnlyList<MetaPreview>> SearchAsync(string term, string type, int limit, CancellationToken ct)`
    - `public Task<IReadOnlyList<ApiStream>> ResolveStreamsAsync(string type, string id, int? max, CancellationToken ct)`
  - `public sealed class ApiStream { Url, Label, Title, Name, Description, NotWebReady }` (moved out of the controller; controller reuses it).
  - Behavior:
    - Add movie: resolve streams (dedupe by URL) to verify playability, then write a single token .strm via `OnDemandLibrary.WriteMovieAsync` with quality = `request.Quality ?? config.DefaultQuality` (the best stream is chosen at play time), call `libraryMonitor.ReportFileSystemChanged(Plugin.StreamRoot)`.
    - Add series: `GetMetaAsync` for the episode list; per episode resolve streams to verify playability, write one token .strm per episode via `WriteShowAsync` (the best stream is chosen at play time); skip episodes with no streams (count skipped in logs only). Report `ReportFileSystemChanged` once after all writes.
    - Quality for tokens: if `request.Quality` empty → `Plugin.Instance.Configuration.DefaultQuality`; token quality value passes through as-is.
    - `SearchAsync`: manifest → search catalog (same logic as v1 controller `SearchAsync`).
    - `ResolveStreamsAsync`: `GetStreamsAsync` → map to `ApiStream` list, dedupe by URL, sorted by `StreamResolver.Rank` so the picker shows best first; `max` caps the list (`MaxStreamsShown` passed by the controller).
    - URL dedupe + "only streams with Url" rules copied from v1 `CatalogSynchronizer.ResolveStreamsAsync`.
    - Thin: no try/catch here (controller handles 400s); log at Information/Debug.

- [ ] **Step 1: Write ApiStream.cs**

```csharp
namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// A stream as exposed to the plugin UI.
/// </summary>
public sealed class ApiStream
{
    public string? Url { get; set; }

    public string? Label { get; set; }

    public string? Title { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public bool? NotWebReady { get; set; }
}
```

- [ ] **Step 2: Write OnDemandService.cs**

```csharp
using Jellyfin.Plugin.AIOStreams.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AIOStreams.Services;

/// <summary>
/// Request to add a single title to the stream library.
/// </summary>
public sealed class TitleAddRequest
{
    public string Type { get; set; } = "movie";

    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? ReleaseInfo { get; set; }

    /// <summary>
    /// Gets or sets the desired quality ("auto", "2160p", ...). Falls back to the plugin DefaultQuality.
    /// </summary>
    public string? Quality { get; set; }
}

/// <summary>
/// Result of an add operation.
/// </summary>
public sealed record AddResult(int Movies, int Shows, int Episodes, int Streams, int Files);

/// <summary>
/// On-demand orchestration: search the addon, add titles to /data/stream as signed .strm files,
/// and remove them again. Each add triggers an incremental library scan via ILibraryMonitor.
/// </summary>
public sealed class OnDemandService
{
    private readonly AIOStreamsClient _client;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<OnDemandService> _logger;
    private readonly SemaphoreSlim _addLock = new(1, 1);

    public OnDemandService(AIOStreamsClient client, ILibraryMonitor libraryMonitor, ILogger<OnDemandService> logger)
    {
        _client = client;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Searches the addon's search catalog.
    /// </summary>
    public async Task<IReadOnlyList<MetaPreview>> SearchAsync(string term, string type, int limit, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        var manifest = await _client.GetManifestAsync(config.AddonUrl, config.ExtraQuery, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not fetch the AIOStreams manifest.");

        var searchCatalog = (manifest.Catalogs ?? [])
            .FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)
                && c.Id?.Contains("search", StringComparison.OrdinalIgnoreCase) == true);

        if (searchCatalog is null)
        {
            return Array.Empty<MetaPreview>();
        }

        var response = await _client.GetCatalogAsync(
                config.AddonUrl,
                config.ExtraQuery,
                type,
                searchCatalog.Id!,
                0,
                Math.Clamp(limit, 1, 100),
                term,
                cancellationToken)
            .ConfigureAwait(false);

        return (response?.Metas ?? [])
            .Where(m => !string.Equals(m.Type, "error", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Resolves the playable streams for a title/episode, ranked best first.
    /// </summary>
    public async Task<IReadOnlyList<ApiStream>> ResolveStreamsAsync(string type, string id, int? max, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        var response = await _client.GetStreamsAsync(config.AddonUrl, config.ExtraQuery, type, id, cancellationToken).ConfigureAwait(false);

        var ranked = StreamResolver.Rank((response?.Streams ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()));

        var result = new List<ApiStream>();
        foreach (var stream in ranked)
        {
            if (max is > 0 && result.Count >= max)
            {
                break;
            }

            result.Add(new ApiStream
            {
                Url = stream.Url,
                Label = stream.Title ?? stream.Name ?? $"Stream {result.Count + 1}",
                Title = stream.Title,
                Name = stream.Name,
                Description = stream.Description,
                NotWebReady = stream.BehaviorHints?.NotWebReady
            });
        }

        return result;
    }

    /// <summary>
    /// Adds a title (movie or series) to the stream library: verifies streams exist,
    /// writes signed .strm files, and triggers an incremental library scan.
    /// </summary>
    public async Task<AddResult> AddTitleAsync(TitleAddRequest request, string playbackBaseUrl, CancellationToken cancellationToken)
    {
        var config = RequireConfig();
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            throw new ArgumentException("A title id is required.");
        }

        var root = Plugin.Instance?.StreamRoot
            ?? throw new InvalidOperationException("Plugin is not loaded.");

        var type = request.Type.Equals("series", StringComparison.OrdinalIgnoreCase) ? "series" : "movie";
        var quality = string.IsNullOrWhiteSpace(request.Quality) ? config.DefaultQuality : request.Quality.Trim();

        await _addLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var meta = await _client.GetMetaAsync(config.AddonUrl, config.ExtraQuery, type, request.Id, cancellationToken).ConfigureAwait(false);
            var title = !string.IsNullOrWhiteSpace(request.Name)
                ? request.Name
                : meta?.Meta?.Name ?? request.Id;
            var releaseInfo = request.ReleaseInfo ?? meta?.Meta?.ReleaseInfo;
            var year = StreamFolder.ExtractYear(releaseInfo);
            var imdbId = StreamFolder.ExtractImdbId(request.Id);

            AddResult result;
            if (type == "series")
            {
                result = await AddSeriesAsync(config, root, title, year, imdbId, quality, playbackBaseUrl, meta?.Meta, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await AddMovieAsync(config, root, title, year, imdbId, request.Id, quality, playbackBaseUrl, cancellationToken).ConfigureAwait(false);
            }

            _libraryMonitor.ReportFileSystemChanged(root);
            return result;
        }
        finally
        {
            _addLock.Release();
        }
    }

    /// <summary>
    /// Removes a title's folder from the stream library and triggers a scan.
    /// </summary>
    public async Task<bool> RemoveTitleAsync(string type, string title, string? year, CancellationToken cancellationToken)
    {
        await _addLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Plugin.Instance?.StreamRoot
                ?? throw new InvalidOperationException("Plugin is not loaded.");
            var removed = OnDemandLibrary.RemoveTitle(root, type, title, year);
            if (removed)
            {
                _libraryMonitor.ReportFileSystemChanged(root);
            }

            return removed;
        }
        finally
        {
            _addLock.Release();
        }
    }

    private async Task<AddResult> AddMovieAsync(
        PluginConfiguration config,
        string root,
        string title,
        string? year,
        string? imdbId,
        string rawId,
        string quality,
        string playbackBaseUrl,
        CancellationToken cancellationToken)
    {
        var streams = await ResolveStreamsAsync("movie", rawId, null, cancellationToken).ConfigureAwait(false);
        if (streams.Count == 0)
        {
            throw new InvalidOperationException("No playable streams were found for this title.");
        }

        var token = BuildToken(config, "movie", rawId, quality);
        var playbackUrl = $"{playbackBaseUrl}/AIOStreams/Stream?token={Uri.EscapeDataString(token)}";
        var written = await OnDemandLibrary.WriteMovieAsync(root, title, year, imdbId, playbackUrl, cancellationToken).ConfigureAwait(false);
        return new AddResult(Movies: 1, Shows: 0, Episodes: 0, Streams: written.Strms, Files: written.Files);
    }

    private async Task<AddResult> AddSeriesAsync(
        PluginConfiguration config,
        string root,
        string title,
        string? year,
        string? imdbId,
        string quality,
        string playbackBaseUrl,
        MetaFull? meta,
        CancellationToken cancellationToken)
    {
        var videos = meta?.Videos ?? [];
        var episodes = new List<EpisodeEntry>();
        var skipped = 0;

        foreach (var video in videos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(video.Id))
            {
                continue;
            }

            var streams = await ResolveStreamsAsync("series", video.Id, null, cancellationToken).ConfigureAwait(false);
            if (streams.Count == 0)
            {
                skipped++;
                continue;
            }

            var token = BuildToken(config, "series", video.Id, quality);
            episodes.Add(new EpisodeEntry(
                video.Season ?? 1,
                video.Episode ?? 1,
                $"{playbackBaseUrl}/AIOStreams/Stream?token={Uri.EscapeDataString(token)}"));
        }

        if (episodes.Count == 0)
        {
            throw new InvalidOperationException("No playable streams were found for this series.");
        }

        if (skipped > 0)
        {
            _logger.LogInformation("Skipped {Skipped} episodes without playable streams.", skipped);
        }

        var written = await OnDemandLibrary.WriteShowAsync(root, title, year, imdbId, episodes, cancellationToken).ConfigureAwait(false);
        return new AddResult(Movies: 0, Shows: 1, Episodes: episodes.Count, Streams: written.Strms, Files: written.Files);
    }

    private static string BuildToken(PluginConfiguration config, string type, string id, string quality)
        => new PlaybackTokenService(config.PlaybackSecret).IssueToken(type, id, quality);

    private static PluginConfiguration RequireConfig()
    {
        var plugin = Plugin.Instance
            ?? throw new InvalidOperationException("Plugin is not loaded.");
        var config = plugin.Configuration;

        if (string.IsNullOrWhiteSpace(config.AddonUrl))
        {
            throw new InvalidOperationException("The AIOStreams addon URL is not configured. Open the plugin settings first.");
        }

        return config;
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet build Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release`
Expected: BUILD SUCCEEDED. (The old `SyncResult`/`CatalogSynchronizer` references in the controller will now fail — Task 8 rewrites the controller; if the build fails only on controller references, that is expected and acceptable; verify the two rewritten files compile by checking error list only mentions the controller/registrator.)

- [ ] **Step 4: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Configuration/PluginConfiguration.cs Jellyfin.Plugin.AIOStreams/Plugin.cs
git commit -m "feat: v2 configuration and required /data/stream root"
```

---

### Task 8: Controller v2 (endpoints: Status, Search, Streams, Add, Remove, Library, Stream, CreateFolder, hook.js)

**Files:**
- Rewrite: `Jellyfin.Plugin.AIOStreams/Api/AIOStreamsController.cs`
- Delete: `Jellyfin.Plugin.AIOStreams/Services/CatalogSynchronizer.cs`
- Delete: `Jellyfin.Plugin.AIOStreams/Services/StrmLibrary.cs`

**Interfaces:**
- Consumes: `OnDemandService` (Task 7), `ApiStream` (Task 7), `AIOStreamsClient`, `PlaybackTokenService` (Task 2), `StreamResolver` (Task 4: `StreamFolder`), `Plugin` (Task 6).
- Produces (final API surface):
  - `GET /AIOStreams/Status` (elevation) → `PluginStatus { PluginVersion, AddonUrlConfigured, FolderState, StreamRoot, QualityPickerAtAdd, AddonName? }` (calls `Plugin.Instance.EnsureStreamFolder()` first).
  - `GET /AIOStreams/Search?term=&type=&limit=` (elevation) — same shape as v1, delegating to `OnDemandService.SearchAsync`; returns `IReadOnlyList<MetaPreview>`.
  - `GET /AIOStreams/Streams?type=&id=&max=` (elevation) — delegates to `ResolveStreamsAsync`, `max` defaults to config `MaxStreamsShown`.
  - `POST /AIOStreams/Add` (elevation) — body `{ Type, Id, Name, ReleaseInfo, Quality }`; builds `playbackBaseUrl` from the request: use `X-Forwarded-Proto`/`X-Forwarded-Host` headers when present, else `Request.Scheme` + `Request.Host`; calls `Plugin.Instance.EnsurePlaybackSecret()` before adding; returns `AddResult`. 400 on `InvalidOperationException`/`ArgumentException`.
  - `POST /AIOStreams/Remove` (elevation) — body `{ Type, Title, Year }`; returns `bool`.
  - `GET /AIOStreams/Library` (elevation) — `OnDemandLibrary.List(Plugin.StreamRoot)` wrapped in `LibraryListing { RootPath, Titles }`.
  - `POST /AIOStreams/CreateFolder` (elevation) — `StreamFolder.Create(Plugin.StreamRoot)`, returns `FolderState.ToString()`.
  - `GET /AIOStreams/Stream?token=` **`[AllowAnonymous]`** — verify token (403 on invalid); fetch streams via `AIOStreamsClient.GetStreamsAsync`; `StreamResolver.Select(streams, payload.Quality)`; if none → 503 "No playable stream was found"; if `stream.NotWebReady == true` → **proxy**: GET the URL server-side with `Referer: <stream url origin>` + `User-Agent` set, stream the body back with the remote content-type; else → `Redirect(stream.Url)` (302).
  - `GET /AIOStreams/WebUI/hook.js` **`[AllowAnonymous]`** — returns the embedded `Web/hook.js` as `text/javascript`.
- Status DTO classes live in the controller file.

- [ ] **Step 1: Write the controller (full rewrite)**

```csharp
using Jellyfin.Plugin.AIOStreams.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AIOStreams.Api;

/// <summary>
/// Body of the Remove request.
/// </summary>
public sealed class RemoveTitleRequest
{
    public string Type { get; set; } = "movie";

    public string Title { get; set; } = string.Empty;

    public string? Year { get; set; }
}

/// <summary>
/// The managed stream folder contents.
/// </summary>
public sealed class LibraryListing
{
    public string RootPath { get; set; } = string.Empty;

    public IReadOnlyList<TitleOnDisk> Titles { get; set; } = [];
}

/// <summary>
/// Plugin status for the UI.
/// </summary>
public sealed class PluginStatus
{
    public string PluginVersion { get; set; } = string.Empty;

    public bool AddonUrlConfigured { get; set; }

    public string FolderState { get; set; } = string.Empty;

    public string StreamRoot { get; set; } = string.Empty;

    public bool QualityPickerAtAdd { get; set; }

    public string? AddonName { get; set; }
}

/// <summary>
/// REST endpoints for the plugin: status, search, stream listing, add/remove, and the
/// unauthenticated playback redirect endpoint used by .strm files.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AIOStreams")]
public class AIOStreamsController : ControllerBase
{
    private readonly AIOStreamsClient _client;
    private readonly OnDemandService _onDemand;
    private readonly ILogger<AIOStreamsController> _logger;

    public AIOStreamsController(
        AIOStreamsClient client,
        OnDemandService onDemand,
        ILogger<AIOStreamsController> logger)
    {
        _client = client;
        _onDemand = onDemand;
        _logger = logger;
    }

    /// <summary>
    /// Returns plugin status including the /data/stream validation state.
    /// </summary>
    [HttpGet("Status")]
    public async Task<ActionResult<PluginStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return BadRequest("Plugin is not loaded.");
        }

        var folderState = plugin.EnsureStreamFolder();
        var config = plugin.Configuration;
        string? addonName = null;
        if (!string.IsNullOrWhiteSpace(config.AddonUrl))
        {
            var manifest = await _client.GetManifestAsync(config.AddonUrl, config.ExtraQuery, cancellationToken).ConfigureAwait(false);
            addonName = manifest?.Name;
        }

        return Ok(new PluginStatus
        {
            PluginVersion = plugin.Version?.ToString() ?? typeof(AIOStreamsController).Assembly.GetName().Version?.ToString() ?? "unknown",
            AddonUrlConfigured = !string.IsNullOrWhiteSpace(config.AddonUrl),
            FolderState = folderState,
            StreamRoot = Plugin.StreamRoot,
            QualityPickerAtAdd = config.QualityPickerAtAdd,
            AddonName = addonName
        });
    }

    /// <summary>
    /// Searches the addon's search catalog.
    /// </summary>
    [HttpGet("Search")]
    public async Task<ActionResult<IReadOnlyList<MetaPreview>>> SearchAsync(
        [FromQuery] string term,
        [FromQuery] string type = "movie",
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest("A search term is required.");
            }

            return Ok(await _onDemand.SearchAsync(term, type, limit, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Resolves and returns the playable streams for a title or episode, ranked best first.
    /// </summary>
    [HttpGet("Streams")]
    public async Task<ActionResult<IReadOnlyList<ApiStream>>> GetStreamsAsync(
        [FromQuery] string type,
        [FromQuery] string id,
        [FromQuery] int? max = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest("A title id is required.");
            }

            var cap = max is > 0 ? max : Plugin.Instance?.Configuration.MaxStreamsShown;
            return Ok(await _onDemand.ResolveStreamsAsync(type, id, cap, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Adds a title to the stream library (incremental, does not wipe existing content).
    /// </summary>
    [HttpPost("Add")]
    public async Task<ActionResult<AddResult>> AddAsync(TitleAddRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var plugin = Plugin.Instance;
            if (plugin is null)
            {
                return BadRequest("Plugin is not loaded.");
            }

            var folderState = plugin.EnsureStreamFolder();
            if (folderState != FolderState.Ok.ToString())
            {
                return BadRequest($"The stream folder {Plugin.StreamRoot} is not usable (state: {folderState}). Create it or enable auto-create in the plugin settings.");
            }

            plugin.EnsurePlaybackSecret();
            return Ok(await _onDemand.AddTitleAsync(request, BuildPlaybackBaseUrl(), cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Removes a title's folder from the stream library.
    /// </summary>
    [HttpPost("Remove")]
    public async Task<ActionResult<bool>> RemoveAsync(RemoveTitleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest("A title is required.");
            }

            return Ok(await _onDemand.RemoveTitleAsync(request.Type, request.Title, request.Year, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Lists the titles currently on disk in the stream folder.
    /// </summary>
    [HttpGet("Library")]
    public ActionResult<LibraryListing> GetLibrary()
    {
        var root = Plugin.Instance?.StreamRoot;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return Ok(new LibraryListing());
        }

        return Ok(new LibraryListing
        {
            RootPath = root,
            Titles = OnDemandLibrary.List(root)
        });
    }

    /// <summary>
    /// Creates the /data/stream folder (used by the "Create now" button).
    /// </summary>
    [HttpPost("CreateFolder")]
    public ActionResult<string> CreateFolder()
    {
        var root = Plugin.Instance?.StreamRoot;
        if (string.IsNullOrEmpty(root))
        {
            return BadRequest("Plugin is not loaded.");
        }

        StreamFolder.Create(root);
        return Ok(StreamFolder.Validate(root).ToString());
    }

    /// <summary>
    /// Playback endpoint referenced by generated .strm files. Validates the HMAC token,
    /// resolves a fresh stream from AIOStreams, then redirects (or proxies when the
    /// stream needs custom request headers). Unauthenticated by design.
    /// </summary>
    [HttpGet("Stream")]
    [AllowAnonymous]
    public async Task<ActionResult> PlayAsync([FromQuery] string token, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return Unauthorized();
        }

        var secret = plugin.Configuration.PlaybackSecret;
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("Playback request with no playback secret configured.");
            return Unauthorized();
        }

        var tokenService = new PlaybackTokenService(secret);
        if (!tokenService.TryVerify(token, out var payload) || payload is null)
        {
            _logger.LogWarning("Playback request rejected: invalid token.");
            return Unauthorized();
        }

        var config = plugin.Configuration;
        var response = await _client.GetStreamsAsync(config.AddonUrl, config.ExtraQuery, payload.Type, payload.Id, cancellationToken).ConfigureAwait(false);
        var streams = (response?.Streams ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var selected = StreamResolver.Select(streams, payload.Quality);
        if (selected is null)
        {
            _logger.LogWarning("No playable stream found for {Type}/{Id}", payload.Type, payload.Id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "No playable stream was found.");
        }

        if (selected.BehaviorHints?.NotWebReady == true)
        {
            return await ProxyAsync(selected.Url!, cancellationToken).ConfigureAwait(false);
        }

        return Redirect(selected.Url!);
    }

    /// <summary>
    /// Serves the optional web-UI hook script (Custom JavaScript integration).
    /// </summary>
    [HttpGet("WebUI/hook.js")]
    [AllowAnonymous]
    public ActionResult GetHookJs()
    {
        var assembly = typeof(AIOStreamsController).Assembly;
        var resource = $"{assembly.GetName().Name}.Web.hook.js";
        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "text/javascript", enableRangeProcessing: false);
    }

    private async Task<ActionResult> ProxyAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var origin = new Uri(url).GetLeftPart(UriPartial.Authority);
            request.Headers.Referrer = new Uri(origin);
            using var response = await _client.SendPlaybackAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Playback proxy failed: {Url} -> {Status}", url, (int)response.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, "The stream source failed to respond.");
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return File(body, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _logger.LogWarning(ex, "Playback proxy failed for {Url}", url);
            return StatusCode(StatusCodes.Status502BadGateway, "The stream source failed to respond.");
        }
    }

    private string BuildPlaybackBaseUrl()
    {
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
        return $"{scheme}://{host}";
    }
}
```

- [ ] **Step 2: Add `SendPlaybackAsync` to AIOStreamsClient**

The proxy path needs a raw playback GET on the shared client. Add to `Services/AIOStreamsClient.cs` (file is otherwise unchanged):

```csharp
    /// <summary>
    /// Issues a raw GET for playback proxying (headers supplied by the caller).
    /// </summary>
    public async Task<HttpResponseMessage> SendPlaybackAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 3: Delete obsolete services**

```bash
git rm Jellyfin.Plugin.AIOStreams/Services/CatalogSynchronizer.cs Jellyfin.Plugin.AIOStreams/Services/StrmLibrary.cs
```

- [ ] **Step 4: Build to verify compilation**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet build Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release`
Expected: BUILD SUCCEEDED (remaining errors only if any — fix them; the old registrator's `CatalogSynchronizer` registration is fixed in Task 12).

- [ ] **Step 5: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Api/AIOStreamsController.cs Jellyfin.Plugin.AIOStreams/Services/AIOStreamsClient.cs
git commit -m "feat: v2 API — on-demand endpoints and signed playback redirect"
```

---

### Task 9: Sidebar search page v2

**Files:**
- Rewrite: `Jellyfin.Plugin.AIOStreams/Web/searchPage.html`

**Interfaces:**
- Consumes: endpoints from Task 8 (`Status`, `Search`, `Streams`, `Add`, `Remove`, `Library`, `CreateFolder`).
- Produces: the main-menu page users search from.

Requirements (keep the existing page's defensive JS patterns — `pick()`, `currentPage()`, `escapeHtml()`, `ApiClient` usage, PascalCase writes, `data-jellyio-init` guard):

1. **Setup banner** at top when `Status.FolderState != "Ok"` or `AddonUrlConfigured == false`:
   - Missing folder: text "The required /data/stream folder is missing." + **Create now** button (POST `AIOStreams/CreateFolder`) when `AutoCreateStreamFolder` is off; when on, note it will be created automatically.
   - Not writable/NotDirectory: red warning with the state name.
   - Addon URL missing: "Open the plugin settings and enter your AIOStreams URL first." + Settings link (`configurationpage?name=Jellyio Streams`).
   - Hide search controls while in a broken setup state.
2. **Search row**: Type select (movie/series), text input, Search button (Enter key works).
3. **Results grid**: poster cards with Add button. When `Status.QualityPickerAtAdd == true`, the Add button opens the quality picker: fetch `AIOStreams/Streams?type=&id=` (max from server), render a modal list of `Label` options + "Best (auto)", each with an Add; Add posts `AIOStreams/Add` with `quality` set. When picker is off, Add posts directly with `quality: null`.
4. **URL params**: read `query` and `type` from the location URL (`window.location.hash`/search) and prefill + auto-search on load (used by the hook.js "Find on AIOStreams" card).
5. **Library section**: list from `AIOStreams/Library` (`Titles[]` with Name/Year/Type), each row with a **Remove** button (confirm dialog, POST `AIOStreams/Remove`, refresh list).
6. **Status line**: plugin version, addon name, folder state (e.g. "addon: My AIOStreams · folder: Ok").

The page must be self-contained HTML (no external assets), same structure as today: `.JellyioSearchPage` div, scoped `init(currentPage())`, `pageshow` re-init.

- [ ] **Step 1: Write the new searchPage.html**

Full rewrite — follow the existing file's scaffolding (IIFE, `currentPage()`, `pick`, `escapeHtml`, `loading` helpers) and add: `status()` (sets banner + state flags, called on init and after every mutation), `search()`, `openPicker(btn)` (modal built with `document.createElement` + class `jellyio-picker`), `add(id, type, name, release, quality)`, `remove(title)` with `confirm()`, `library()`, URL-param prefill. Use PascalCase in config writes (not needed here — config page only). All POSTs via `ApiClient.ajax` with `contentType: "application/json"`. `Add` result shows "Added — Jellyfin is scanning now."

No test cycle beyond build (page is embedded; validate by build + a manual check that the file is well-formed).

- [ ] **Step 2: Build to verify the resource is still embedded**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet build Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Web/searchPage.html
git commit -m "feat: v2 search page with setup banner, quality picker, remove"
```

---

### Task 10: Web UI hook script (hook.js)

**Files:**
- Create: `Jellyfin.Plugin.AIOStreams/Web/hook.js`
- Modify: `Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj` (embed the file)

**Interfaces:**
- Consumes: `/AIOStreams/WebUI/hook.js` endpoint (Task 8) and the sidebar page with `?query=`/`?type=` params (Task 9).
- Produces: the one-line snippet users paste into Dashboard → General → Custom JavaScript:
  `<script src="/AIOStreams/WebUI/hook.js"></script>`

Behavior (runs inside Jellyfin web):
1. **Library toolbar button:** every 2s (max ~60 attempts), check whether the current view is a library item grid (`location.hash` matches `#/movies.html`, `#/series.html`, `#/collections.html`, or `#/home.html` with a `parentId`). When it is, fetch `ApiClient.getUrl("AIOStreams/Status")`; when `FolderState == "Ok"` and the current `parentId` maps to the stream library, inject a button "Search AIOStreams" into the library page toolbar (detect via `.pageLibraryPage .header`/`libraryToolbar` — use a MutationObserver on `document.body` for robustness). Identify the stream library by calling `/Library/VirtualFolders` (admin) and matching `folder.Location` against `Status.StreamRoot`; cache the folder ItemId.
2. **Button click** → `Dashboard.navigate("pluginpage?name=JellyioStreamsSearch")`.
3. **Global search card:** when the hash is `#/search.html` and the results list is empty, inject a card into `.searchResults` (or `#searchPage .itemsContainer`): "Find on AIOStreams: '<query>'" with a button that navigates to `pluginpage?name=JellyioStreamsSearch&query=<term>&type=movie`.
4. Everything wrapped in try/catch; no-op on any failure; never throws.

- [ ] **Step 1: Write hook.js**

```javascript
(function () {
    "use strict";
    var BUTTON_CLASS = "jellyio-hook-search";
    var attempts = 0;

    function status() {
        return window.ApiClient && window.ApiClient.getJSON
            ? window.ApiClient.getJSON(window.ApiClient.getUrl("AIOStreams/Status"))
            : Promise.resolve(null);
    }

    function streamLibraryId() {
        return status().then(function (st) {
            if (!st || st.FolderState !== "Ok") { return null; }
            return window.ApiClient.getJSON(window.ApiClient.getUrl("Library/VirtualFolders")).then(function (folders) {
                var found = null;
                (folders || []).forEach(function (f) {
                    var loc = f.Location || [];
                    if (loc.indexOf(st.StreamRoot) >= 0 || loc.indexOf(st.StreamRoot + "/") === 0) {
                        found = f.ItemId;
                    }
                });
                return found;
            }).catch(function () { return null; });
        }).catch(function () { return null; });
    }

    function currentParentId() {
        var m = (window.location.hash || "").match(/parentId=([^&]+)/);
        return m ? decodeURIComponent(m[1]) : null;
    }

    function ensureButton(parentId) {
        if (!parentId || document.querySelector("." + BUTTON_CLASS)) { return; }
        streamLibraryId().then(function (libId) {
            if (!libId || libId !== parentId) { return; }
            var toolbar = document.querySelector(".pageLibraryPage .header, .libraryPage .header, .header");
            if (!toolbar) { return; }
            var btn = document.createElement("button");
            btn.className = BUTTON_CLASS + " button-link emby-button";
            btn.style.cssText = "margin-left:1em;";
            btn.textContent = "Search AIOStreams";
            btn.addEventListener("click", function () {
                if (window.Dashboard && Dashboard.navigate) {
                    Dashboard.navigate("pluginpage?name=JellyioStreamsSearch");
                }
            });
            toolbar.appendChild(btn);
        });
    }

    function ensureSearchCard() {
        var hash = window.location.hash || "";
        if (hash.indexOf("#/search.html") !== 0) { return; }
        var term = decodeURIComponent((hash.match(/query=([^&]*)/) || [])[1] || "");
        if (!term || document.querySelector(".jellyio-hook-searchcard")) { return; }
        var container = document.querySelector(".searchResults, #searchPage .itemsContainer");
        if (!container) { return; }
        var empty = !container.querySelector(".card");
        if (!empty) { return; }
        var card = document.createElement("div");
        card.className = "jellyio-hook-searchcard card";
        card.style.cssText = "padding:1.5em;text-align:center;";
        var p = document.createElement("p");
        p.textContent = "Not in your library? Find \"" + term + "\" on AIOStreams.";
        var btn = document.createElement("button");
        btn.textContent = "Search AIOStreams";
        btn.addEventListener("click", function () {
            if (window.Dashboard && Dashboard.navigate) {
                Dashboard.navigate("pluginpage?name=JellyioStreamsSearch&query=" + encodeURIComponent(term) + "&type=movie");
            }
        });
        card.appendChild(p);
        card.appendChild(btn);
        container.appendChild(card);
    }

    function tick() {
        try {
            ensureButton(currentParentId());
            ensureSearchCard();
        } catch (e) { /* never throw */ }
        attempts++;
        if (attempts < 120) { setTimeout(tick, 2000); }
    }

    setTimeout(tick, 3000);
})();
```

- [ ] **Step 2: Embed the resource in the csproj**

Add inside the existing `<ItemGroup>` with the other embedded resources:

```xml
    <None Remove="Web\hook.js" />
    <EmbeddedResource Include="Web\hook.js" />
```

- [ ] **Step 3: Build to verify**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet build Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Web/hook.js Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj
git commit -m "feat: web UI hook script for in-library search"
```

---

### Task 11: Configuration page v2

**Files:**
- Rewrite: `Jellyfin.Plugin.AIOStreams/Configuration/configPage.html`

**Interfaces:**
- Consumes: Jellyfin config endpoints (existing), `AIOStreams/Status` + `AIOStreams/CreateFolder` (Task 8).
- Produces: the settings page.

Requirements (follow existing page conventions; PascalCase reads/writes; scoped lookups; re-read config after save):

1. Fields: **Addon URL** (text), **Extra query params** (text), **Auto-create /data/stream** (checkbox), **Quality picker at add time** (checkbox), **Default quality** (select: auto/2160p/1080p/720p), **Max streams shown in picker** (number).
2. **Setup status box**: shows `AIOStreams/Status` (version, addon name, folder state); when folder missing and auto-create off → "Create now" button (POST `AIOStreams/CreateFolder`, re-poll status).
3. **Web UI hook box**: textarea containing `<script src="/AIOStreams/WebUI/hook.js"></script>` with a **Copy** button (uses `navigator.clipboard.writeText` with fallback `select()`+`document.execCommand("copy")`), plus 3 steps: Dashboard → General → Custom JavaScript → paste → Save.
4. **Test connection** button: POST nothing, but GET `AIOStreams/Status` and display addon name/error (keep existing behavior: save config first, then test).
5. Keep the existing "Save" flow: build PascalCase config from the form, POST `/Configuration/<pluginId>`, handle 204 as success, re-read after save, refuse to save when the initial config read failed.

- [ ] **Step 1: Write configPage.html**

Follow the v1 file's structure (read it first — `Configuration/configPage.html`), replacing the settings sections with the fields above and adding the setup-status box, hook box, and Create-now button. Keep: the PascalCase read/write helpers, `currentPage()` scoping, defensive `Dashboard`/`ApiClient` guards, error display, global error hook.

- [ ] **Step 2: Build to verify**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet build Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/Configuration/configPage.html
git commit -m "feat: v2 config page with hook snippet and folder status"
```

---

### Task 12: Registrator, cleanup, version bump, docs

**Files:**
- Rewrite: `Jellyfin.Plugin.AIOStreams/PluginServiceRegistrator.cs`
- Modify: `Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj` (Version `2.0.0.0`)
- Delete: `Jellyfin.Plugin.AIOStreams/Tasks/RefreshTask.cs`
- Rewrite: `README.md`, `manifest.json`, `build.yaml`

**Interfaces:**
- Consumes: everything.
- Produces: final installable plugin build.

- [ ] **Step 1: Rewrite the registrator**

```csharp
using Jellyfin.Plugin.AIOStreams.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AIOStreams;

/// <summary>
/// Registers plugin services with the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        var version = typeof(PluginServiceRegistrator).Assembly.GetName().Version?.ToString(3) ?? "1.0";

        serviceCollection.AddHttpClient<AIOStreamsClient>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Jellyfin.Plugin.AIOStreams/{version}");
        });

        serviceCollection.AddSingleton<OnDemandService>();
    }
}
```

- [ ] **Step 2: Remove the scheduled task and bump the version**

```bash
git rm Jellyfin.Plugin.AIOStreams/Tasks/RefreshTask.cs
```

Edit `Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj`: `<Version>1.0.4.0</Version>` → `<Version>2.0.0.0</Version>`.

- [ ] **Step 3: Full build + tests**

Run: `export PATH="$PATH:/home/will/.dotnet" && dotnet build Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release && dotnet test tests/Jellyfin.Plugin.AIOStreams.Tests/Jellyfin.Plugin.AIOStreams.Tests.csproj`
Expected: BUILD SUCCEEDED, all unit tests PASS.

- [ ] **Step 4: Rewrite README.md**

Cover: what it is (Stremio-like on-demand), requirements (Jellyfin 10.11, self-hosted AIOStreams with user config), install, setup (1: create `/data/stream` (or enable auto-create), 2: add `Stream` library of type Mixed Movies and Shows pointing at `/data/stream`, 3: configure addon URL, 4: search & add from the sidebar page), the optional Custom JS hook (with the one-line snippet + where to paste), stream behavior (auto best / quality picker toggle, expired-link fallback, header proxy), API table (Task 8 surface), configuration reference (Task 6 fields), notes & limitations (playback URL is the Jellyfin address used when adding — re-add after changing host; search page is web-only, playback works on all clients), roadmap (subtitles, per-user configs, catalog browsing), license.

- [ ] **Step 5: Update manifest.json and build.yaml**

- `manifest.json`: bump description (on-demand, /data/stream, signed redirect playback, all clients); add version `2.0.0.0` entry at the top of `versions` with `targetAbi: "10.11.0.0"`, `sourceUrl` v2.0.0.0, `checksum` `"00000000000000000000000000000000"` (computed at release time), `timestamp` `2026-08-12T00:00:00Z`, changelog "v2: on-demand search & add; signed playback redirect (fresh streams, works on all clients); required /data/stream folder (TRaSH layout); optional in-library search via Custom JS hook."
- `build.yaml`: `version: "2.0.0.0"`, updated description/changelog.

- [ ] **Step 6: Commit**

```bash
git add Jellyfin.Plugin.AIOStreams/PluginServiceRegistrator.cs Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj README.md manifest.json build.yaml
git commit -m "feat!: v2.0.0 — on-demand redirect architecture"
```

---

### Task 13: Manual verification checklist

**Files:** none (documentation of the manual QA flow — perform against a real Jellyfin + AIOStreams instance).

- [ ] **Step 1: Install & configure**

1. Publish: `export PATH="$PATH:/home/will/.dotnet" && dotnet publish Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release`
2. Copy `bin/Release/net9.0/Jellyfin.Plugin.AIOStreams.dll` (plus `Jellyfin.Plugin.AIOStreams.pdb`) to `<jellyfin data dir>/plugins/AIOStreams/`; restart Jellyfin.
3. In a fresh setup (no `/data/stream`): the sidebar page shows the setup banner; with auto-create on, Status reports `FolderState` becomes `Ok` after the first visit (or via "Create now" when off).
4. Configure the addon URL in plugin settings; "Test connection" shows the addon name.

- [ ] **Step 2: Add & play on web**

1. Create a "Stream" library (Mixed Movies and Shows) at `/data/stream`; scan once.
2. Sidebar → Jellyio Streams → search a movie → Add. The library shows the item within seconds (incremental scan).
3. Play it: the server hits `/AIOStreams/Stream?token=…` and 302-redirects; playback starts. Verify with `curl -I` on the token URL that a 302 (or 200 for proxied streams) is returned.
4. Toggle "Quality picker at add time" on, add another title, confirm the picker modal appears and the chosen quality is baked into the token (decode the token payload — `t/i/q/e` fields).
5. Add a series: episodes appear as `S01E01` etc.; play an episode.
6. Remove a title from the library section; confirm the folder is deleted and the item disappears after the scan.

- [ ] **Step 3: Other clients**

1. Android/iOS app: browse the Stream library, play a movie and an episode.
2. Kodi (Jellyfin addon) or Jellyfin Media Player: play the same items.
3. Confirm no client-specific code paths are needed (all playback goes through the server redirect).

- [ ] **Step 4: Hook + failure paths**

1. Paste the hook snippet into Dashboard → General → Custom JavaScript; browse the Stream library → "Search AIOStreams" button appears; global search with a nonsense term shows the "Find on AIOStreams" card.
2. Kill the AIOStreams instance; play a title → 503/502 with a logged warning; restart AIOStreams → playback works again (live resolution).
3. Tamper a token URL → 403 Unauthorized.

## Self-Review Notes

- Spec coverage: required folder + auto-create toggle → Task 4/6; signed redirect playback (option A) → Task 2/3/8; quality picker toggle → Task 3/6/9; on-demand (no sync) → Task 7/8 + RefreshTask removed (Task 12); sidebar search + JS hook → Task 9/10/11; cross-client → server-side redirect in Task 8 + manual checklist Task 13; TRaSH layout → Task 4/5; config surface → Task 6/11; API surface → Task 8; error handling → Task 8 (403/503/502/504-logs, setup gating); testing → Tasks 1–5 unit + Task 13 manual.
- Placeholder scan: no TBD/TODO; all code blocks are complete except HTML pages, which specify exact behavior and reference the existing file as the structural template (existing-codebase convention).
- Type consistency: `PlaybackTokenService(IssueToken/TryVerify/GenerateSecret)`, `StreamResolver(Rank/Select/ResolveQuality)`, `StreamFolder(Validate/Create/MovieDir/TvShowDir/EpisodeFileName/ExtractYear/ExtractImdbId)`, `OnDemandLibrary(WriteMovieAsync/WriteShowAsync/RemoveTitle/List)`, `OnDemandService(SearchAsync/ResolveStreamsAsync/AddTitleAsync/RemoveTitleAsync)`, `ApiStream`, `TitleAddRequest`, `AddResult`, `Plugin.StreamRoot`, `Plugin.EnsurePlaybackSecret()`, `Plugin.EnsureStreamFolder()` — names consistent across all tasks.
