# Fenpo Protocol

Single source of truth for every message exchanged between Fenpo apps. Windows (C#), iPhone (Swift) and Android (Kotlin) code is generated from these files; never hand-write message types.

| File | Contents |
|---|---|
| `fenpo/v1/common.proto` | `DevicePlatform`, `Capability` |
| `fenpo/v1/hello.proto` | `Hello`, `Ping`, `Pong`, `Error` |
| `fenpo/v1/pairing.proto` | QR format, `PairingRequest`, `PairingResult` |
| `fenpo/v1/content.proto` | Clipboard, open URL |
| `fenpo/v1/transfer.proto` | File transfer negotiation and progress |
| `fenpo/v1/device.proto` | Device state, ring, lock |
| `fenpo/v1/envelope.proto` | `Envelope`, field ranges per feature group |

## Generation

| Platform | Tool | Output |
|---|---|---|
| Windows | `Grpc.Tools` (messages only, no services) in `Fenpo.Protocol.csproj` | `obj/` at build time |
| iPhone | `protoc` + `protoc-gen-swift` built from the swift-protobuf version `FenpoKit` resolves, run by `ios/scripts/generate-proto.sh` (SwiftPM build plugins cannot read files outside the package) | before building, into `ios/FenpoKit/Sources/FenpoProtocol/Generated/` (ignored by git) |
| Android | protobuf Gradle plugin, Kotlin lite (or Wire, decided in Android phase A) | build time |

Include path is this `protocol/` folder, so imports are written as `fenpo/v1/<file>.proto`.

## Fenpo BLE channel

The PC is the GATT server, the phone the client. These UUIDs are fixed for every PC and every app version; the iPhone app declares the service UUID in `NSAccessorySetupBluetoothServices`.

| Item | UUID | Properties |
|---|---|---|
| Fenpo service | `1888047E-C8C5-45CC-AAEA-333A8859E79F` | Primary, advertised |
| `rx` (phone → PC) | `ECC8D0D1-990A-4B02-A4FC-49FA647E7254` | Write without response, encryption required |
| `tx` (PC → phone) | `E9EC01E0-8C02-4B8E-902C-4F113E25DFC8` | Notify, encryption required |

Requiring encryption on both characteristics makes the first access trigger iOS pairing (bonding), which the PC then uses to discover ANCS.

## Rules

- This is a draft of version 1. Until the first public release, fields may change; after that, only additive changes.
- Never reuse or renumber a field. Remove with `reserved`.
- New features get a `Capability` value and stay inside their `Envelope` field range.
- A protocol change is its own commit (`protocol: ...`) and updates `docs/ARCHITECTURE.md` in the same commit.
