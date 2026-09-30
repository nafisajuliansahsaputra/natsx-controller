using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class SmartHandoverCoordinator
{
    private readonly SmartConnectionManager _manager;
    private readonly ControllerSession _session;
    private readonly InputSafetyEngine _safetyEngine;

    private HandoverProposal? _pendingProposal;

    public SmartHandoverCoordinator(
        SmartConnectionManager manager,
        ControllerSession session,
        InputSafetyEngine safetyEngine)
    {
        _manager = manager ??
            throw new ArgumentNullException(nameof(manager));
        _session = session ??
            throw new ArgumentNullException(nameof(session));
        _safetyEngine = safetyEngine ??
            throw new ArgumentNullException(nameof(safetyEngine));
    }

    public HandoverProposal? PendingProposal =>
        _pendingProposal;

    public bool HasPendingHandover =>
        _pendingProposal is not null;

    public void ActivateInitial(
        HandoverProposal proposal)
    {
        if (proposal.From is not null ||
            proposal.Reason !=
                HandoverReason.InitialSelection)
        {
            throw new ArgumentException(
                "Initial activation requires an initial-selection proposal.",
                nameof(proposal));
        }

        if (_manager.ActiveTransport is not null)
        {
            throw new InvalidOperationException(
                "An active transport already exists.");
        }

        _session.BeginNewSession(proposal.To);
        _manager.Commit(proposal);
        _pendingProposal = null;
    }

    public bool BeginHandover(
        HandoverProposal proposal)
    {
        if (proposal.From is null ||
            _manager.ActiveTransport != proposal.From ||
            _session.AuthoritativeTransport != proposal.From)
        {
            return false;
        }

        if (proposal.To == proposal.From)
            return false;

        AbortPendingHandover();

        _pendingProposal = proposal;
        return true;
    }

    public bool TrySynchronizeCandidate(
        TransportKind transport,
        uint globalSequence,
        GamepadState state)
    {
        HandoverProposal? proposal =
            _pendingProposal;

        if (proposal is null ||
            proposal.Value.To != transport)
        {
            return false;
        }

        return _safetyEngine.TryStageHandoverState(
            transport,
            globalSequence,
            state);
    }

    public bool TryCommitHandover()
    {
        HandoverProposal? proposal =
            _pendingProposal;

        if (proposal is null)
            return false;

        if (!_safetyEngine.TryCommitHandover(
                proposal.Value.To))
        {
            return false;
        }

        _manager.Commit(proposal.Value);
        _pendingProposal = null;
        return true;
    }

    public void AbortPendingHandover()
    {
        if (_pendingProposal is not
            HandoverProposal proposal)
        {
            return;
        }

        _safetyEngine.AbortHandover(
            proposal.To);
        _pendingProposal = null;
    }

    public void LoseAllTransports()
    {
        AbortPendingHandover();
        _manager.ClearActiveTransport();
        _safetyEngine.LoseAllTransports();
    }
}
