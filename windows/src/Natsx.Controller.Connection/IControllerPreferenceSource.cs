using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public interface IControllerPreferenceSource
{
    event Action<TransportKind?>?
        PreferredTransportRequested;
}
