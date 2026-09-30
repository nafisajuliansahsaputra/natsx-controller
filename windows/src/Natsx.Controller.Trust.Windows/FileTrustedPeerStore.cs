using System.Security.Cryptography;
using System.Text.Json;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Trust.Windows;

public sealed class FileTrustedPeerStore : ITrustedPeerStore
{
    private readonly string _path;
    private readonly ITrustSecretProtector _protector;
    private readonly object _gate = new();

    public FileTrustedPeerStore(
        string path,
        ITrustSecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _path = path;
    }

    public void Put(
        TrustedPeerRecord record,
        ReadOnlySpan<byte> trustSecret)
    {
        if (trustSecret.Length != TrustedPeerMaterial.TrustSecretSize)
        {
            throw new ArgumentException(
                $"Trust secret must be exactly {TrustedPeerMaterial.TrustSecretSize} bytes.",
                nameof(trustSecret));
        }

        byte[] protectedSecret = _protector.Protect(trustSecret);

        try
        {
            lock (_gate)
            {
                TrustDocument document = ReadDocument();
                string id = record.PeerId.ToString();

                document.Peers[id] = new StoredPeer
                {
                    PeerId = id,
                    DisplayName = record.DisplayName,
                    Capabilities = record.Capabilities,
                    PairedAtUtc = record.PairedAt.ToUniversalTime(),
                    PairingVersion = record.PairingVersion,
                    ProtectedSecret = Convert.ToBase64String(protectedSecret),
                };

                WriteDocument(document);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedSecret);
        }
    }

    public TrustedPeerMaterial? Get(PeerId peerId)
    {
        lock (_gate)
        {
            TrustDocument document = ReadDocument();
            if (!document.Peers.TryGetValue(peerId.ToString(), out StoredPeer? stored))
            {
                return null;
            }

            byte[] protectedSecret = Convert.FromBase64String(stored.ProtectedSecret);
            byte[] plaintext = _protector.Unprotect(protectedSecret);

            try
            {
                var record = new TrustedPeerRecord(
                    peerId,
                    stored.DisplayName,
                    stored.Capabilities,
                    stored.PairedAtUtc,
                    stored.PairingVersion);

                return new TrustedPeerMaterial(record, plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedSecret);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public IReadOnlyList<TrustedPeerRecord> List()
    {
        lock (_gate)
        {
            return ReadDocument().Peers.Values
                .Select(ToRecord)
                .OrderByDescending(static record => record.PairedAt)
                .ToArray();
        }
    }

    public void Remove(PeerId peerId)
    {
        lock (_gate)
        {
            TrustDocument document = ReadDocument();
            if (document.Peers.Remove(peerId.ToString()))
            {
                WriteDocument(document);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            WriteDocument(new TrustDocument());
        }
    }

    private static TrustedPeerRecord ToRecord(StoredPeer stored)
    {
        return new TrustedPeerRecord(
            ParsePeerId(stored.PeerId),
            stored.DisplayName,
            stored.Capabilities,
            stored.PairedAtUtc,
            stored.PairingVersion);
    }

    private TrustDocument ReadDocument()
    {
        if (!File.Exists(_path))
        {
            return new TrustDocument();
        }

        string json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TrustDocument();
        }

        return JsonSerializer.Deserialize<TrustDocument>(json, JsonOptions)
            ?? new TrustDocument();
    }

    private void WriteDocument(TrustDocument document)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = _path + ".tmp";
        string json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }

    private static PeerId ParsePeerId(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        return PeerId.FromBytes(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private sealed class TrustDocument
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, StoredPeer> Peers { get; set; } =
            new(StringComparer.Ordinal);
    }

    private sealed class StoredPeer
    {
        public required string PeerId { get; set; }

        public string? DisplayName { get; set; }

        public byte Capabilities { get; set; }

        public DateTimeOffset PairedAtUtc { get; set; }

        public int PairingVersion { get; set; } =
            TrustedPeerRecord.CurrentPairingVersion;

        public required string ProtectedSecret { get; set; }
    }
}
