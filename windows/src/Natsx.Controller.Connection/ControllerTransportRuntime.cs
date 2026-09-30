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
    private readonly IReadOnlyDictionary<TransportKind, IControllerTransport> _transports;
    private readonly Dictionary<TransportKind, LatestTransportState> _latestStates = new();
    private readonly object _connectionGate = new();
    private readonly object _stateGate = new();

    private CancellationTokenSource? _lifetime;
    private Task? _evaluationLoop;
    private bool _started;
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

        var map = new Dictionary<TransportKind, IControllerTransport>();
        foreach (IControllerTransport transport in transports)
        {
            if (!map.TryAdd(transport.Kind, transport))
            {
                throw new ArgumentException(
                    $"Only one transport instance per kind is allowed: {transport.Kind}.",
                    nameof(transports));
            }

            transport.GamepadStateReceived += OnGamepadStateReceived;
            transport.StateChanged += OnTransportStateChanged;
        }

        _transports = map;
    }

    public event Action<HandoverProposal>? HandoverCommitted;

    public event Action<TransportKind, Exception>? TransportFaulted;

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

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _started = true;
        _lifetime = new CancellationTokenSource();

        foreach (IControllerTransport transport in _transports.Values)
        {
            try
            {
                ReportTransportHealth(transport);

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
        _evaluationLoop = EvaluationLoopAsync(_lifetime.Token);
    }

    public void EvaluateOnce()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_connectionGate)
        {
            foreach (IControllerTransport transport in _transports.Values)
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

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            return;
        }

        _started = false;

        CancellationTokenSource? lifetime = _lifetime;
        Task? loop = _evaluationLoop;
        _lifetime = null;
        _evaluationLoop = null;

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

        foreach (IControllerTransport transport in _transports.Values)
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
        if (sender is not IControllerTransport transport)
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

            if (_session.LastAcceptedSequence is uint previousSequence &&
                !SequenceNumber.IsNewer(candidate.Sequence, previousSequence))
            {
                return false;
            }

            TransportKind? oldAuthority = _session.AuthoritativeTransport;
            _session.SetAuthoritativeTransport(proposal.To);

            if (!_inputSafety.TryAccept(
                    proposal.To,
                    candidate.Sequence,
                    candidate.State))
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
                _transports.TryGetValue(
                    previousAuthorityKind,
                    out IControllerTransport? previousTransport))
            {
                previousTransport.SetAuthoritative(false);
            }

            if (_transports.TryGetValue(
                    proposal.To,
                    out IControllerTransport? newTransport))
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);

        foreach (IControllerTransport transport in _transports.Values)
        {
            transport.GamepadStateReceived -= OnGamepadStateReceived;
            transport.StateChanged -= OnTransportStateChanged;
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
