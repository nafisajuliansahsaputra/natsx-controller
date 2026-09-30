using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public sealed class PairingInvitation : IDisposable
{
    public const string Scheme = "natsx";
    public const string Host = "pair";
    public const string VersionPath = "/v1";

    private readonly byte[] _secret;
    private bool _disposed;

    public PairingInvitation(
        PeerId receiverPeerId,
        ReadOnlySpan<byte> secret,
        long expiresUnixSeconds)
    {
        if (secret.Length != PairingCrypto.SecretSize)
        {
            throw new ArgumentException(
                $"Pairing invitation secret must be exactly {PairingCrypto.SecretSize} bytes.",
                nameof(secret));
        }

        ReceiverPeerId = receiverPeerId;
        ExpiresUnixSeconds = expiresUnixSeconds;
        _secret = secret.ToArray();
    }

    public PeerId ReceiverPeerId { get; }

    public long ExpiresUnixSeconds { get; }

    public byte[] CopySecret()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _secret.ToArray();
    }

    public bool IsExpired(DateTimeOffset now) =>
        now.ToUnixTimeSeconds() > ExpiresUnixSeconds;

    public string ToUriString()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return $"{Scheme}://{Host}{VersionPath}" +
            $"?peer={ReceiverPeerId}" +
            $"&secret={Base64UrlEncode(_secret)}" +
            $"&expires={ExpiresUnixSeconds}";
    }

    public static PairingInvitation Create(
        PeerId receiverPeerId,
        DateTimeOffset expiresAt)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(PairingCrypto.SecretSize);

        try
        {
            return new PairingInvitation(
                receiverPeerId,
                secret,
                expiresAt.ToUnixTimeSeconds());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public static PairingInvitation Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, VersionPath, StringComparison.Ordinal))
        {
            throw new FormatException("Invalid NATSX pairing invitation URI.");
        }

        Dictionary<string, string> query = ParseQuery(uri.Query);

        if (!query.TryGetValue("peer", out string? peerValue) ||
            !query.TryGetValue("secret", out string? secretValue) ||
            !query.TryGetValue("expires", out string? expiresValue))
        {
            throw new FormatException("Pairing invitation is missing required fields.");
        }

        byte[] peerBytes;
        byte[] secret;

        try
        {
            peerBytes = Convert.FromHexString(peerValue);
            secret = Base64UrlDecode(secretValue);
        }
        catch (FormatException exception)
        {
            throw new FormatException("Pairing invitation contains invalid encoded data.", exception);
        }

        try
        {
            if (!long.TryParse(
                    expiresValue,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out long expiresUnixSeconds))
            {
                throw new FormatException("Pairing invitation expiry is invalid.");
            }

            return new PairingInvitation(
                PeerId.FromBytes(peerBytes),
                secret,
                expiresUnixSeconds);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public override string ToString() =>
        $"PairingInvitation(ReceiverPeerId={ReceiverPeerId}, ExpiresUnixSeconds={ExpiresUnixSeconds}, Secret=[redacted])";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_secret);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                throw new FormatException("Pairing invitation query is malformed.");
            }

            string key = Uri.UnescapeDataString(parts[0]);
            string value = Uri.UnescapeDataString(parts[1]);

            if (!values.TryAdd(key, value))
            {
                throw new FormatException($"Duplicate pairing invitation field: {key}.");
            }
        }

        return values;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        int padding = (4 - base64.Length % 4) % 4;
        base64 += new string('=', padding);
        return Convert.FromBase64String(base64);
    }
}
