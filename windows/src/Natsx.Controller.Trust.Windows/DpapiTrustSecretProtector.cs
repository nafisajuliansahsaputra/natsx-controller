using System.Security.Cryptography;

namespace Natsx.Controller.Trust.Windows;

public sealed class DpapiTrustSecretProtector : ITrustSecretProtector
{
    private static readonly byte[] OptionalEntropy =
        "NATSX-TRUST-STORE-V1"u8.ToArray();

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows DPAPI trust protection requires Windows.");
        }

        return ProtectedData.Protect(
            plaintext.ToArray(),
            OptionalEntropy,
            DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows DPAPI trust protection requires Windows.");
        }

        return ProtectedData.Unprotect(
            protectedData.ToArray(),
            OptionalEntropy,
            DataProtectionScope.CurrentUser);
    }
}
