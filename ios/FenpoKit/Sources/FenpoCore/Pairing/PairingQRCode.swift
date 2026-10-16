import Foundation

/// The pairing QR code shown by the PC (protocol/fenpo/v1/pairing.proto):
///
///     fenpo://pair?v=1&id=<pcId>&n=<url-encoded name>&a=<ip:port,ip:port>&fp=<hex sha256 of PC cert>&t=<base64url token>
public struct PairingQRCode: Equatable, Sendable {
    /// Why a scanned text is not a usable pairing code.
    public enum ParseError: Error, Equatable, Sendable {
        /// Not a `fenpo://pair` link at all, e.g. some other QR code.
        case notPairingLink

        /// A newer pairing format this app does not understand.
        case unsupportedVersion(String)

        /// A required query field is missing or empty.
        case missingField(String)

        /// An entry of `a` is not `host:port`.
        case invalidAddress(String)

        /// `fp` is not 64 hex digits.
        case invalidFingerprint

        /// `t` is not 16 bytes of base64url.
        case invalidToken
    }

    /// Pairing format version this app understands.
    public static let supportedVersion = "1"

    /// Stable id of the PC.
    public let pcID: String

    /// User visible PC name.
    public let pcName: String

    /// Addresses of the PC's data channel, in the order the PC listed them.
    public let endpoints: [Endpoint]

    /// SHA-256 of the PC's TLS certificate (32 bytes). The server certificate must match it.
    public let certificateSHA256: [UInt8]

    /// One-time pairing token (16 bytes).
    public let token: [UInt8]

    /// Parses the text of a scanned QR code.
    /// - Parameter text: Contents of the QR code.
    public init(parsing text: String) throws(ParseError) {
        // Parsed by hand: `a` carries "[" and "]" for IPv6, which strict URL parsers reject in a query.
        let text = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let prefix = "fenpo://pair?"
        guard text.count > prefix.count, text.prefix(prefix.count).lowercased() == prefix else {
            throw .notPairingLink
        }

        var fields: [String: String] = [:]
        for pair in text.dropFirst(prefix.count).split(separator: "&") {
            let parts = pair.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
            let name = String(parts[0])
            let raw = parts.count > 1 ? String(parts[1]) : ""
            if fields[name] == nil {
                fields[name] = raw.removingPercentEncoding ?? raw
            }
        }
        func required(_ name: String) throws(ParseError) -> String {
            guard let value = fields[name], !value.isEmpty else {
                throw .missingField(name)
            }
            return value
        }

        let version = try required("v")
        guard version == Self.supportedVersion else {
            throw .unsupportedVersion(version)
        }

        pcID = try required("id")
        pcName = try required("n")

        var endpoints: [Endpoint] = []
        for entry in try required("a").split(separator: ",") {
            guard let endpoint = Endpoint(parsing: String(entry)) else {
                throw .invalidAddress(String(entry))
            }
            endpoints.append(endpoint)
        }
        guard !endpoints.isEmpty else {
            throw .missingField("a")
        }
        self.endpoints = endpoints

        guard let fingerprint = Hex.decode(try required("fp")), fingerprint.count == 32 else {
            throw .invalidFingerprint
        }
        certificateSHA256 = fingerprint

        guard let token = Base64URL.decode(try required("t")), token.count == 16 else {
            throw .invalidToken
        }
        self.token = token
    }
}

/// A host and port of the PC's data channel.
public struct Endpoint: Equatable, Hashable, Sendable, CustomStringConvertible {
    /// IPv4 address, IPv6 address (without brackets, may carry a `%zone`) or host name.
    public let host: String

    /// TCP port.
    public let port: UInt16

    /// Creates an endpoint.
    /// - Parameters:
    ///   - host: Address or host name.
    ///   - port: TCP port.
    public init(host: String, port: UInt16) {
        self.host = host
        self.port = port
    }

    /// Parses `host:port`, `[ipv6]:port` or `[ipv6%zone]:port`.
    /// - Parameter text: Text to parse.
    public init?(parsing text: String) {
        let text = text.trimmingCharacters(in: .whitespaces)
        let host: Substring
        let portText: Substring
        if text.hasPrefix("[") {
            guard let close = text.firstIndex(of: "]") else {
                return nil
            }
            host = text[text.index(after: text.startIndex)..<close]
            let rest = text[text.index(after: close)...]
            guard rest.hasPrefix(":") else {
                return nil
            }
            portText = rest.dropFirst()
        } else {
            guard let colon = text.lastIndex(of: ":") else {
                return nil
            }
            host = text[..<colon]
            portText = text[text.index(after: colon)...]
            // An unbracketed IPv6 address has more colons; it cannot carry a port.
            guard !host.contains(":") else {
                return nil
            }
        }
        guard !host.isEmpty, let port = UInt16(portText), port > 0 else {
            return nil
        }
        self.host = String(host)
        self.port = port
    }

    public var description: String {
        host.contains(":") ? "[\(host)]:\(port)" : "\(host):\(port)"
    }
}
