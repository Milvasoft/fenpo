import Foundation

/// Encodes and decodes a value through JSON, as the stores persist it.
enum JSONDecoderShim {
    static func roundTrip<T: Codable>(_ value: T) throws -> T {
        try JSONDecoder().decode(T.self, from: JSONEncoder().encode(value))
    }
}

/// Minimal DER reader for checking what `DER` and `SelfSignedCertificate` write.
struct DERNode: Equatable {
    let tag: UInt8
    let content: [UInt8]

    /// Children of a constructed node.
    var children: [DERNode] {
        get throws {
            try DERNode.parseAll(content)
        }
    }

    static func parse(_ bytes: [UInt8]) throws -> DERNode {
        var offset = 0
        let node = try read(bytes, at: &offset)
        guard offset == bytes.count else {
            throw DERReadError.trailingBytes
        }
        return node
    }

    static func parseAll(_ bytes: [UInt8]) throws -> [DERNode] {
        var nodes: [DERNode] = []
        var offset = 0
        while offset < bytes.count {
            nodes.append(try read(bytes, at: &offset))
        }
        return nodes
    }

    private static func read(_ bytes: [UInt8], at offset: inout Int) throws -> DERNode {
        guard offset + 2 <= bytes.count else {
            throw DERReadError.truncated
        }
        let tag = bytes[offset]
        var length = Int(bytes[offset + 1])
        offset += 2
        if length & 0x80 != 0 {
            let count = length & 0x7F
            guard count > 0, offset + count <= bytes.count else {
                throw DERReadError.truncated
            }
            length = bytes[offset..<(offset + count)].reduce(0) { $0 << 8 | Int($1) }
            offset += count
        }
        guard offset + length <= bytes.count else {
            throw DERReadError.truncated
        }
        let content = Array(bytes[offset..<(offset + length)])
        offset += length
        return DERNode(tag: tag, content: content)
    }
}

enum DERReadError: Error {
    case truncated
    case trailingBytes
}
