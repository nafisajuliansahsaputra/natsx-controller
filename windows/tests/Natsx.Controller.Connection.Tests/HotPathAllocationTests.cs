using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Connection.Tests;

public sealed class HotPathAllocationTests
{
    [Fact]
    public void AcceptedRealtimeState_CoreHotPathDoesNotAllocatePerFrame()
    {
        var clock =
            new IncrementingTimeProvider();
        var backend =
            new AllocationProbeBackend();
        var session =
            new ControllerSession();

        session.SetAuthoritativeTransport(
            TransportKind.Wifi);

        var engine =
            new InputSafetyEngine(
                session,
                backend,
                ConnectionPolicy.Competitive,
                clock);

        GamepadState state =
            GamepadState.Neutral with
            {
                Buttons =
                    GamepadButtons.A |
                    GamepadButtons.RightShoulder,
                LeftX = 12_000,
                LeftY = -8_000,
                RightX = 2_000,
                RightY = 5_000,
                RightTrigger = 220,
            };

        uint sequence =
            1;

        for (int index = 0;
             index < WarmupIterations;
             index++)
        {
            if (!engine.TryAccept(
                    TransportKind.Wifi,
                    sequence++,
                    state))
            {
                throw new InvalidOperationException(
                    "Hot-path warmup state was unexpectedly rejected.");
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before =
            GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0;
             index < MeasuredIterations;
             index++)
        {
            if (!engine.TryAccept(
                    TransportKind.Wifi,
                    sequence++,
                    state))
            {
                throw new InvalidOperationException(
                    "Measured hot-path state was unexpectedly rejected.");
            }
        }

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() -
            before;

        Assert.True(
            allocated <= AllocationBudgetBytes,
            $"Accepted-state core path allocated {allocated:N0} bytes across {MeasuredIterations:N0} frames; budget is {AllocationBudgetBytes:N0} bytes total.");

        Assert.Equal(
            MeasuredIterations +
            WarmupIterations,
            backend.SubmitCount);
    }

    private const int WarmupIterations =
        10_000;

    private const int MeasuredIterations =
        100_000;

    private const long AllocationBudgetBytes =
        32 * 1024;

    private sealed class IncrementingTimeProvider :
        TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp() =>
            Interlocked.Increment(
                ref _timestamp);
    }

    private sealed class AllocationProbeBackend :
        IVirtualGamepadBackend
    {
        public bool IsStarted =>
            true;

        public int SubmitCount { get; private set; }

        public event Action<RumbleState>? RumbleReceived
        {
            add
            {
            }

            remove
            {
            }
        }

        public ValueTask StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public void Submit(
            GamepadState state)
        {
            SubmitCount++;
        }

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
