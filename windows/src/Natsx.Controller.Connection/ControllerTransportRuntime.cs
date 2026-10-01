using System.Threading.Channels;
using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

/// <summary>
/// Coordinates transport health and authoritative state routing while keeping
/// the virtual gamepad lifecycle independent from transport lifecycle.
/// </summary>
public sealed class ControllerTransportRuntime : IAsyncDisposable
{
    private readonly ControllerSession _session;
    private readonly InputSafetyEngine _inputSafety;
    private readonly SmartConnectionManager _connectionManager;
    private readonly ConnectionPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<TransportKind, IControllerTransport> _transports = new();
    private readonly Dictionary<TransportKind, LatestTransportState> _latestStates = new();
    private readonly Dictionary<TransportKind, PacketRateTracker> _packetRates = new();
    private readonly HashSet<TransportKind> _seenTransportKinds = new();
    private readonly Channel<RumbleState> _rumbleQueue =
        Channel.CreateBounded<RumbleState>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
    private readonly object _transportGate = new();
    private readonly object _connectionGate = new();
    private readonly object _stateGate = new();

    private CancellationTokenSource? _lifetime;
    private Task? _evaluationLoop;
    private Task? _rumbleLoop;
    private bool _started;
    private bool _suppressLifecycleReports;
    private int _reconnectCount;
    private bool _disposed;

    public ControllerTransportRuntime(
        ControllerSession session,
        InputSafetyEngine inputSafety,
        SmartConnectionManager connectionManager,
        IEnumerable<IControllerTransport> transports,
        ConnectionPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _inputSafety = inputSafety ?? throw new ArgumentNullException(nameof(inputSafety));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _policy = policy ?? ConnectionPolicy.Competitive;
        _timeProvider = timeProvider ?? TimeProvider.System;

        ArgumentNullException.ThrowIfNull(transports);

        foreach (IControllerTransport transport in transports)
        {
            AddTransportCore(
                transport,
                nameof(transports));
        }
    }

    public event Action<HandoverProposal>? HandoverCommitted;

    public event Action<TransportKind, Exception>? TransportFaulted;

    public event Action<TransportKind, Exception>? OutputFaulted;

    public TransportKind? ActiveTransport
    {
        get
        {
            lock (_connectionGate)
            {
                return _connectionManager.ActiveTransport;
            }
        }
    }

    public ConnectionManagerState State
    {
        get
        {
            lock (_connectionGate)
            {
                return _connectionManager.State;
            }
        }
    }

    public bool TryGetTransportState(
        TransportKind kind,
        out TransportRuntimeState state)
    {
        if (TryGetTransport(
                kind,
                out IControllerTransport transport))
        {
            state =
                transport.State;
            return true;
        }

        state =
            TransportRuntimeState.Unavailable;
        return false;
    }

    public bool TryGetTransportHealthSnapshot(
        TransportKind kind,
        out TransportHealthSnapshot snapshot)
    {
        if (TryGetTransport(
                kind,
                out IControllerTransport transport))
        {
            try
            {
                snapshot =
                    transport.GetHealthSnapshot();
                return true;
            }
            catch
            {
            }
        }

        snapshot =
            default;
        return false;
    }

    public double GetInputRateHz(
        TransportKind kind)
    {
        lock (_stateGate)
        {
            if (!_packetRates.TryGetValue(
                    kind,
                    out PacketRateTracker? tracker))
            {
                return 0;
            }

            return tracker.GetRateHz(
                _timeProvider);
        }
    }

    public int ReconnectCount
    {
        get
        {
            lock (_transportGate)
            {
                return _reconnectCount;
            }
        }
    }

    /// <summary>
    /// Queues the newest rumble state without blocking the virtual-controller
    /// callback or realtime input path. When output falls behind, stale rumble
    /// states are discarded in favor of the newest state.
    /// </summary>
    public bool TrySubmitRumble(
        RumbleState rumble)
    {
        if (!_started ||
            _disposed)
        {
            return false;
        }

        return _rumbleQueue.Writer
            .TryWrite(
                rumble);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _started = true;
        _suppressLifecycleReports = false;
        _lifetime = new CancellationTokenSource();

        foreach (IControllerTransport transport in GetTransportSnapshot())
        {
            try
            {
                if (transport.State != TransportRuntimeState.Unavailable)
                {
                    ReportTransportHealth(transport);
                }

                await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

                ReportTransportHealth(transport);
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                TransportFaulted?.Invoke(transport.Kind, exception);
            }
        }

        EvaluateOnce();
        _evaluationLoop =
            EvaluationLoopAsync(
                _lifetime.Token);
        _rumbleLoop =
            RumbleLoopAsync(
                _lifetime.Token);
    }

    public void EvaluateOnce()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IControllerTransport[] transports =
            GetTransportSnapshot();

        lock (_connectionGate)
        {
            foreach (IControllerTransport transport in transports)
            {
                ReportTransportHealth(transport);
            }

            HandoverProposal? proposal = _connectionManager.Evaluate();
            if (proposal is HandoverProposal value)
            {
                TryCommitHandover(value);
            }
        }

        lock (_stateGate)
        {
            _inputSafety.Evaluate();
        }
    }

    /// <summary>
    /// Adds a transport candidate after the runtime has been created. This is
    /// required for authenticated Wi-Fi/Bluetooth/USB links whose concrete
    /// session transport only exists after a trusted handshake completes.
    /// Ownership transfers to the runtime after this method succeeds.
    /// </summary>
    public async ValueTask AttachTransportAsync(
        IControllerTransport transport,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(transport);
        cancellationToken.ThrowIfCancellationRequested();

        AddTransportCore(
            transport,
            nameof(transport));

        if (!_started)
        {
            return;
        }

        try
        {
            if (transport.State != TransportRuntimeState.Unavailable)
            {
                ReportTransportHealth(transport);
            }

            await transport
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            ReportTransportHealth(transport);
            EvaluateOnce();
        }
        catch
        {
            RemoveTransportCore(
                transport.Kind,
                transport);

            throw;
        }
    }

    /// <summary>
    /// Removes and disposes a transport without stopping the controller
    /// runtime. If the removed transport is authoritative, Smart Auto gets one
    /// final chance to hand over to a fresh ready backup before authority is
    /// cleared and the safety engine is neutralized.
    /// </summary>
    public async ValueTask<bool> DetachTransportAsync(
        TransportKind kind,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!TryGetTransport(
                kind,
                out IControllerTransport transport))
        {
            return false;
        }

        try
        {
            await transport
                .DisconnectAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            TransportFaulted?.Invoke(
                kind,
                exception);
        }

        ReportTransportHealth(transport);
        EvaluateOnce();

        RemoveTransportCore(
            kind,
            transport);

        lock (_stateGate)
        {
            _latestStates.Remove(kind);
            _packetRates.Remove(kind);

            if (_session.AuthoritativeTransport == kind)
            {
                _inputSafety.ForceNeutral();
                _session.ClearAuthority();
            }
        }

        lock (_connectionGate)
        {
            if (_connectionManager.ActiveTransport == kind)
            {
                _connectionManager.ClearActiveTransport();
            }
        }

        await transport
            .DisposeAsync()
            .ConfigureAwait(false);

        // A failed pre-removal handover can happen when both warm transports
        // carried the same session-global state revision. Once the old
        // transport is gone, immediately re-evaluate remaining candidates
        // instead of waiting for the periodic health tick.
        EvaluateOnce();

        return true;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _suppressLifecycleReports = true;

        CancellationTokenSource? lifetime = _lifetime;
        Task? loop = _evaluationLoop;
        Task? rumbleLoop = _rumbleLoop;
        _lifetime = null;
        _evaluationLoop = null;
        _rumbleLoop = null;

        lifetime?.Cancel();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (rumbleLoop is not null)
        {
            try
            {
                await rumbleLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (IControllerTransport transport in GetTransportSnapshot())
        {
            try
            {
                await transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                TransportFaulted?.Invoke(transport.Kind, exception);
            }
        }

        lock (_stateGate)
        {
            _inputSafety.ForceNeutral();
            _session.ClearAuthority();
            _latestStates.Clear();
        }

        lock (_connectionGate)
        {
            _connectionManager.ClearActiveTransport();
        }

        lifetime?.Dispose();
    }

    private async Task RumbleLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                RumbleState rumble in
                _rumbleQueue.Reader
                    .ReadAllAsync(
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                await RouteRumbleAsync(
                        rumble,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask RouteRumbleAsync(
        RumbleState rumble,
        CancellationToken cancellationToken)
    {
        TransportKind? active =
            ActiveTransport;

        IControllerTransport[] snapshot =
            GetTransportSnapshot();

        IEnumerable<IControllerTransport> ordered =
            snapshot
                .OrderBy(
                    transport =>
                        transport.Kind ==
                        active
                            ? 0
                            : transport.Kind switch
                            {
                                TransportKind.Usb => 1,
                                TransportKind.Wifi => 2,
                                TransportKind.Bluetooth => 3,
                                _ => 4,
                            });

        foreach (IControllerTransport transport in ordered)
        {
            if (transport is not IControllerOutputTransport output ||
                transport.State is
                    TransportRuntimeState.Unavailable or
                    TransportRuntimeState.Failed)
            {
                continue;
            }

            try
            {
                if (await output
                    .TrySendRumbleAsync(
                        rumble,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                OutputFaulted?.Invoke(
                    transport.Kind,
                    exception);
            }
        }
    }

    private async Task EvaluationLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            _policy.HealthEvaluationInterval,
            _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                EvaluateOnce();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnTransportStateChanged(
        object? sender,
        TransportRuntimeStateChangedEventArgs eventArgs)
    {
        if (_suppressLifecycleReports ||
            sender is not IControllerTransport transport)
        {
            return;
        }

        ReportTransportHealth(transport);
    }

    private void ReportTransportHealth(IControllerTransport transport)
    {
        lock (_connectionGate)
        {
            try
            {
                _connectionManager.Report(
                    transport.GetHealthSnapshot());
            }
            catch (Exception exception)
            {
                TransportFaulted?.Invoke(
                    transport.Kind,
                    exception);
            }
        }
    }

    private void OnGamepadStateReceived(
        object? sender,
        TransportGamepadStateEventArgs eventArgs)
    {
        lock (_stateGate)
        {
            if (!_packetRates.TryGetValue(
                    eventArgs.Transport,
                    out PacketRateTracker? rateTracker))
            {
                rateTracker =
                    new PacketRateTracker();

                _packetRates[eventArgs.Transport] =
                    rateTracker;
            }

            rateTracker.Observe(
                eventArgs.ReceivedTimestamp,
                _timeProvider);

            _latestStates[eventArgs.Transport] = new LatestTransportState(
                eventArgs.Transport,
                eventArgs.Sequence,
                eventArgs.State,
                eventArgs.ReceivedTimestamp);

            if (_session.AuthoritativeTransport == eventArgs.Transport)
            {
                _inputSafety.TryAccept(
                    eventArgs.Transport,
                    eventArgs.Sequence,
                    eventArgs.State);
            }
        }
    }

    private bool TryCommitHandover(HandoverProposal proposal)
    {
        HandoverProposal? committed = null;

        lock (_stateGate)
        {
            if (!_latestStates.TryGetValue(proposal.To, out LatestTransportState candidate))
            {
                return false;
            }

            TimeSpan age = _timeProvider.GetElapsedTime(
                candidate.ReceivedTimestamp,
                _timeProvider.GetTimestamp());

            if (age > _policy.FailoverSilence)
            {
                return false;
            }

            TransportKind? oldAuthority =
                _session.AuthoritativeTransport;

            bool accepted;

            if (_session.LastAcceptedSequence is uint previousSequence &&
                candidate.Sequence == previousSequence)
            {
                accepted =
                    _inputSafety
                        .TryAdoptEquivalentState(
                            proposal.To,
                            candidate.Sequence,
                            candidate.State);
            }
            else
            {
                if (_session.LastAcceptedSequence is uint lastAccepted &&
                    !SequenceNumber.IsNewer(
                        candidate.Sequence,
                        lastAccepted))
                {
                    return false;
                }

                _session.SetAuthoritativeTransport(
                    proposal.To);

                accepted =
                    _inputSafety.TryAccept(
                        proposal.To,
                        candidate.Sequence,
                        candidate.State);
            }

            if (!accepted)
            {
                if (oldAuthority is TransportKind previousAuthority)
                {
                    _session.SetAuthoritativeTransport(previousAuthority);
                }
                else
                {
                    _session.ClearAuthority();
                }

                return false;
            }

            _connectionManager.Commit(proposal);

            if (oldAuthority is TransportKind previousAuthorityKind &&
                previousAuthorityKind != proposal.To &&
                TryGetTransport(
                    previousAuthorityKind,
                    out IControllerTransport previousTransport))
            {
                previousTransport.SetAuthoritative(false);
            }

            if (TryGetTransport(
                    proposal.To,
                    out IControllerTransport newTransport))
            {
                newTransport.SetAuthoritative(true);
            }

            committed = proposal;
        }

        if (committed is HandoverProposal value)
        {
            HandoverCommitted?.Invoke(value);
            return true;
        }

        return false;
    }

    private void AddTransportCore(
        IControllerTransport transport,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(transport);

        lock (_transportGate)
        {
            if (!_transports.TryAdd(
                    transport.Kind,
                    transport))
            {
                throw new ArgumentException(
                    $"Only one transport instance per kind is allowed: {transport.Kind}.",
                    parameterName);
            }

            if (!_seenTransportKinds.Add(
                    transport.Kind))
            {
                _reconnectCount++;
            }

            transport.GamepadStateReceived +=
                OnGamepadStateReceived;
            transport.StateChanged +=
                OnTransportStateChanged;
        }
    }

    private bool RemoveTransportCore(
        TransportKind kind,
        IControllerTransport expectedTransport)
    {
        lock (_transportGate)
        {
            if (!_transports.TryGetValue(
                    kind,
                    out IControllerTransport? registered) ||
                !ReferenceEquals(
                    registered,
                    expectedTransport))
            {
                return false;
            }

            registered.GamepadStateReceived -=
                OnGamepadStateReceived;
            registered.StateChanged -=
                OnTransportStateChanged;

            return _transports.Remove(kind);
        }
    }

    private bool TryGetTransport(
        TransportKind kind,
        out IControllerTransport transport)
    {
        lock (_transportGate)
        {
            if (_transports.TryGetValue(
                    kind,
                    out IControllerTransport? registered))
            {
                transport = registered;
                return true;
            }
        }

        transport = null!;
        return false;
    }

    private IControllerTransport[] GetTransportSnapshot()
    {
        lock (_transportGate)
        {
            return _transports.Values.ToArray();
        }
    }

    private sealed class PacketRateTracker
    {
        private static readonly TimeSpan RateWindow =
            TimeSpan.FromMilliseconds(
                500);

        private static readonly TimeSpan StaleAfter =
            TimeSpan.FromSeconds(
                1);

        private bool _hasPackets;
        private long _windowStartedTimestamp;
        private long _lastPacketTimestamp;
        private int _windowPacketCount;
        private double _lastRateHz;

        public void Observe(
            long timestamp,
            TimeProvider timeProvider)
        {
            if (!_hasPackets)
            {
                _hasPackets =
                    true;
                _windowStartedTimestamp =
                    timestamp;
                _lastPacketTimestamp =
                    timestamp;
                _windowPacketCount =
                    1;
                return;
            }

            _lastPacketTimestamp =
                timestamp;
            _windowPacketCount++;

            TimeSpan elapsed =
                timeProvider.GetElapsedTime(
                    _windowStartedTimestamp,
                    timestamp);

            if (elapsed < RateWindow ||
                elapsed <= TimeSpan.Zero)
            {
                return;
            }

            _lastRateHz =
                _windowPacketCount /
                elapsed.TotalSeconds;

            _windowStartedTimestamp =
                timestamp;
            _windowPacketCount =
                0;
        }

        public double GetRateHz(
            TimeProvider timeProvider)
        {
            if (!_hasPackets)
            {
                return 0;
            }

            long now =
                timeProvider.GetTimestamp();

            if (timeProvider.GetElapsedTime(
                    _lastPacketTimestamp,
                    now) >
                StaleAfter)
            {
                return 0;
            }

            TimeSpan currentWindow =
                timeProvider.GetElapsedTime(
                    _windowStartedTimestamp,
                    now);

            if (_windowPacketCount > 0 &&
                currentWindow >
                TimeSpan.Zero)
            {
                return _windowPacketCount /
                    currentWindow.TotalSeconds;
            }

            return _lastRateHz;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);

        IControllerTransport[] transports =
            GetTransportSnapshot();

        foreach (IControllerTransport transport in transports)
        {
            RemoveTransportCore(
                transport.Kind,
                transport);

            await transport
                .DisposeAsync()
                .ConfigureAwait(false);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
