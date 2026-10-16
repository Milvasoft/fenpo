import Foundation

/// Lower-case hexadecimal, as used for certificate fingerprints.
public enum Hex {
    /// Encodes bytes as lower-case hex.
    /// - Parameter bytes: Bytes to encode.
    public static func encode(_ bytes: some Sequence<UInt8>) -> String {
        bytes.map { byte in
            let text = String(byte, radix: 16)
            return byte < 16 ? "0" + text : text
        }
        .joined()
    }

    /// Decodes hex in either case; returns `nil` for odd length or other characters.
    /// - Parameter text: Hex text.
    public static func decode(_ text: String) -> [UInt8]? {
        let digits = Array(text.utf8)
        guard digits.count.isMultiple(of: 2) else {
            return nil
        }
        var bytes: [UInt8] = []
        bytes.reserveCapacity(digits.count / 2)
        var index = 0
        while index < digits.count {
            guard let high = value(of: digits[index]), let low = value(of: digits[index + 1]) else {
                return nil
            }
            bytes.append(high << 4 | low)
            index += 2
        }
        return bytes
    }

    private static func value(of digit: UInt8) -> UInt8? {
        switch digit {
        case UInt8(ascii: "0")...UInt8(ascii: "9"):
            digit - UInt8(ascii: "0")
        case UInt8(ascii: "a")...UInt8(ascii: "f"):
            digit - UInt8(ascii: "a") + 10
        case UInt8(ascii: "A")...UInt8(ascii: "F"):
            digit - UInt8(ascii: "A") + 10
        default:
            nil
        }
    }
}

/// Base64 with the URL-safe alphabet and no padding (RFC 4648 §5), as used for the pairing token.
public enum Base64URL {
    /// Encodes bytes without padding.
    /// - Parameter bytes: Bytes to encode.
    public static func encode(_ bytes: [UInt8]) -> String {
        Data(bytes).base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    /// Decodes with or without padding; returns `nil` for characters outside the URL-safe alphabet.
    /// - Parameter text: Base64url text.
    public static func decode(_ text: String) -> [UInt8]? {
        guard !text.contains("+"), !text.contains("/") else {
            return nil
        }
        var base64 = text
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        while !base64.count.isMultiple(of: 4) {
            base64 += "="
        }
        return Data(base64Encoded: base64).map(Array.init)
    }
}
