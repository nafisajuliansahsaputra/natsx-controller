# Wi-Fi local discovery v1

Discovery helps an Android controller find NATSX Receiver instances on the local network.

Discovery is **not authentication**. A discovery response never grants permission to control the PC. Session authentication still uses the trusted reconnect protocol.

## Transport

- UDP
- discovery port: **37073**
- controller data/control port default: **37074**

Android sends a discovery request to available IPv4 broadcast addresses. Windows replies unicast to the request source address/port.

## Discovery request

Fixed size: **24 bytes**

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | ASCII magic `NXD1` |
| 4 | 1 | Protocol major |
| 5 | 1 | Protocol minor |
| 6 | 1 | Message type = 1 |
| 7 | 1 | Reserved zero |
| 8 | 16 | Android Device ID |

## Discovery response

Variable size: **26 + receiverNameLength bytes**

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | ASCII magic `NXD1` |
| 4 | 1 | Protocol major |
| 5 | 1 | Protocol minor |
| 6 | 1 | Message type = 2 |
| 7 | 1 | Receiver name UTF-8 byte length, 0..63 |
| 8 | 2 | Controller service UDP port |
| 10 | 16 | Windows Device ID |
| 26 | N | Receiver name UTF-8 |

Receiver names are display metadata only and must never be used as trust identity.

## Safety

- malformed discovery packets are ignored;
- unknown major versions are ignored;
- discovery must not create virtual-controller authority;
- only a successfully authenticated trusted session may submit gameplay state.
