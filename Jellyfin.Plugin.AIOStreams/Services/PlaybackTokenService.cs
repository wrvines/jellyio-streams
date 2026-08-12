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
        _key = Base64UrlDecode(secret);
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
