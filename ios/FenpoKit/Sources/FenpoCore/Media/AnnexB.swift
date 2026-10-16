/// Converts H.264/HEVC access units from AVCC (length-prefixed NAL units, what VideoToolbox gives)
/// to Annex-B (start-code-prefixed, what the PC decodes).
public enum AnnexB {
    /// The 4-byte start code.
    public static let startCode: [UInt8] = [0, 0, 0, 1]

    /// Rewrites length prefixes as start codes. Stops at a truncated unit.
    /// - Parameters:
    ///   - avcc: Access unit with big-endian length prefixes.
    ///   - lengthSize: Size of each prefix (1, 2 or 4 bytes).
    public static func from(avcc: [UInt8], lengthSize: Int = 4) -> [UInt8] {
        guard [1, 2, 4].contains(lengthSize) else {
            return []
        }
        var result: [UInt8] = []
        result.reserveCapacity(avcc.count + 16)
        var offset = 0
        while offset + lengthSize <= avcc.count {
            var length = 0
            for index in 0..<lengthSize {
                length = length << 8 | Int(avcc[offset + index])
            }
            offset += lengthSize
            guard length > 0, offset + length <= avcc.count else {
                break
            }
            result += startCode
            result += avcc[offset..<(offset + length)]
            offset += length
        }
        return result
    }
}
