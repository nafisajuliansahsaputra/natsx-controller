namespace Natsx.Controller.Trust.Windows;

public interface ITrustSecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}
