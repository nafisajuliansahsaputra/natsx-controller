using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Natsx.Controller.Receiver;

internal static class WindowsDataProtection
{
    private const uint CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(ReadOnlySpan<byte> plaintext) =>
        Transform(plaintext, protect: true);

    public static byte[] Unprotect(ReadOnlySpan<byte> ciphertext) =>
        Transform(ciphertext, protect: false);

    private static byte[] Transform(ReadOnlySpan<byte> input, bool protect)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is only available on Windows.");

        DATA_BLOB inputBlob = default;
        DATA_BLOB outputBlob = default;

        try
        {
            inputBlob.cbData = input.Length;
            inputBlob.pbData = Marshal.AllocHGlobal(Math.Max(1, input.Length));

            if (!input.IsEmpty)
            {
                byte[] copy = input.ToArray();
                Marshal.Copy(copy, 0, inputBlob.pbData, copy.Length);
                Array.Clear(copy);
            }

            bool success = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);

            if (!success)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var result = new byte[outputBlob.cbData];

            if (outputBlob.cbData > 0)
                Marshal.Copy(outputBlob.pbData, result, 0, outputBlob.cbData);

            return result;
        }
        finally
        {
            if (inputBlob.pbData != IntPtr.Zero)
            {
                ZeroAndFree(inputBlob.pbData, inputBlob.cbData);
            }

            if (outputBlob.pbData != IntPtr.Zero)
            {
                LocalFree(outputBlob.pbData);
            }
        }
    }

    private static void ZeroAndFree(IntPtr pointer, int length)
    {
        if (length > 0)
        {
            byte[] zeroes = new byte[length];
            Marshal.Copy(zeroes, 0, pointer, length);
        }

        Marshal.FreeHGlobal(pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport(
        "crypt32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport(
        "crypt32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
