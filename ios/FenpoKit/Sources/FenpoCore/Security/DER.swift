import Foundation

/// The small subset of ASN.1 DER needed to write a self-signed X.509 certificate.
public enum DER {
    /// Tag, length and value.
    /// - Parameters:
    ///   - tag: Identifier octet.
    ///   - content: Encoded contents.
    public static func tlv(_ tag: UInt8, _ content: [UInt8]) -> [UInt8] {
        [tag] + length(content.count) + content
    }

    /// SEQUENCE of already encoded elements.
    public static func sequence(_ elements: [UInt8]...) -> [UInt8] {
        tlv(0x30, elements.flatMap { $0 })
    }

    /// SET of already encoded elements.
    public static func set(_ elements: [UInt8]...) -> [UInt8] {
        tlv(0x31, elements.flatMap { $0 })
    }

    /// INTEGER from big-endian magnitude bytes; keeps it positive and minimal.
    /// - Parameter magnitude: Unsigned big-endian value.
    public static func integer(_ magnitude: [UInt8]) -> [UInt8] {
        var bytes = Array(magnitude.drop { $0 == 0 })
        if bytes.isEmpty {
            bytes = [0]
        } else if bytes[0] & 0x80 != 0 {
            bytes.insert(0, at: 0)
        }
        return tlv(0x02, bytes)
    }

    /// INTEGER from a small non-negative value.
    /// - Parameter value: Value to encode.
    public static func integer(_ value: UInt64) -> [UInt8] {
        integer((0..<8).reversed().map { UInt8(truncatingIfNeeded: value >> UInt64($0 * 8)) })
    }

    /// OBJECT IDENTIFIER from dotted components.
    /// - Parameter components: Arc values, e.g. `[1, 2, 840, 10045, 2, 1]`.
    public static func objectIdentifier(_ components: [UInt64]) -> [UInt8] {
        precondition(components.count >= 2, "An OID has at least two arcs")
        var content: [UInt8] = [UInt8(components[0] * 40 + components[1])]
        for arc in components.dropFirst(2) {
            var chunk: [UInt8] = [UInt8(arc & 0x7F)]
            var rest = arc >> 7
            while rest > 0 {
                chunk.insert(UInt8(rest & 0x7F) | 0x80, at: 0)
                rest >>= 7
            }
            content += chunk
        }
        return tlv(0x06, content)
    }

    /// UTF8String.
    /// - Parameter text: Text to encode.
    public static func utf8String(_ text: String) -> [UInt8] {
        tlv(0x0C, Array(text.utf8))
    }

    /// BIT STRING with no unused bits.
    /// - Parameter bytes: Bits to encode.
    public static func bitString(_ bytes: [UInt8]) -> [UInt8] {
        tlv(0x03, [0] + bytes)
    }

    /// Explicitly tagged, constructed context-specific element `[n]`.
    /// - Parameters:
    ///   - number: Tag number.
    ///   - content: Encoded inner element.
    public static func contextExplicit(_ number: UInt8, _ content: [UInt8]) -> [UInt8] {
        tlv(0xA0 | number, content)
    }

    /// UTCTime before 2050, GeneralizedTime from 2050 (RFC 5280 §4.1.2.5).
    /// - Parameter date: Time to encode, in UTC.
    public static func time(_ date: Date) -> [UInt8] {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        let parts = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        let year = parts.year!
        func two(_ value: Int) -> String {
            value < 10 ? "0\(value)" : "\(value)"
        }
        let rest = two(parts.month!) + two(parts.day!) + two(parts.hour!) + two(parts.minute!) + two(parts.second!) + "Z"
        if year < 2050 {
            return tlv(0x17, Array((two(year % 100) + rest).utf8))
        }
        return tlv(0x18, Array(("\(year)" + rest).utf8))
    }

    private static func length(_ count: Int) -> [UInt8] {
        if count < 0x80 {
            return [UInt8(count)]
        }
        var bytes: [UInt8] = []
        var rest = count
        while rest > 0 {
            bytes.insert(UInt8(rest & 0xFF), at: 0)
            rest >>= 8
        }
        return [0x80 | UInt8(bytes.count)] + bytes
    }
}
