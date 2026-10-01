using Windows.Storage.Streams;
using Natsx.Controller.Transport.Usb;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class WinRtUsbDirectOutputStreamTests
{
    [Fact]
    public async Task WriteAsync_CommitsEveryWriteInOrder()
    {
        using var memory =
            new InMemoryRandomAccessStream();

        IOutputStream nativeOutput =
            memory.GetOutputStreamAt(0);

        using var output =
            new WinRtUsbDirectOutputStream(
                nativeOutput);

        byte[] first =
            [0x04, 0x00, 0x00, 0x00];

        byte[] second =
            [0x4E, 0x41, 0x54, 0x58];

        await output.WriteAsync(first);
        await output.WriteAsync(second);

        Assert.Equal(
            (ulong)(first.Length + second.Length),
            memory.Size);

        memory.Seek(0);

        using var reader =
            new DataReader(
                memory.GetInputStreamAt(0));

        uint loaded =
            await reader.LoadAsync(
                (uint)memory.Size);

        Assert.Equal(
            (uint)memory.Size,
            loaded);

        byte[] actual =
            new byte[loaded];

        reader.ReadBytes(actual);

        Assert.Equal(
            first.Concat(second),
            actual);
    }

    [Fact]
    public async Task FlushAsync_IsNoOpBecauseWritesAreAlreadyStored()
    {
        using var memory =
            new InMemoryRandomAccessStream();

        using var output =
            new WinRtUsbDirectOutputStream(
                memory.GetOutputStreamAt(0));

        byte[] payload =
            [0x01, 0x02, 0x03, 0x04];

        await output.WriteAsync(payload);
        await output.FlushAsync();

        Assert.Equal(
            (ulong)payload.Length,
            memory.Size);
    }
}
