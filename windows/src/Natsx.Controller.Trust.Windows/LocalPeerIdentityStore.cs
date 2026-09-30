using Natsx.Controller.Protocol;

namespace Natsx.Controller.Trust.Windows;

public sealed class LocalPeerIdentityStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public LocalPeerIdentityStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public PeerId GetOrCreate()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
            {
                string stored = File.ReadAllText(_path).Trim();
                if (!string.IsNullOrWhiteSpace(stored))
                {
                    try
                    {
                        byte[] bytes = Convert.FromHexString(stored);
                        return PeerId.FromBytes(bytes);
                    }
                    catch (Exception exception) when (
                        exception is FormatException or ArgumentException)
                    {
                        // Replace corrupt non-secret identity metadata.
                    }
                }
            }

            PeerId created = PeerId.CreateRandom();
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, created.ToString());
            File.Move(temporary, _path, overwrite: true);
            return created;
        }
    }
}
