import FenpoCore
import Foundation
import Testing

struct FenpoIdentifiersTests {
    @Test
    func bleIdentifiersAreDistinctUUIDs() throws {
        let values = [BLEChannelIdentifiers.service, BLEChannelIdentifiers.rx, BLEChannelIdentifiers.tx]
        let uuids = try values.map { try #require(UUID(uuidString: $0)) }
        #expect(Set(uuids).count == 3)
    }

    @Test(arguments: [
        (nil, AppearancePreference.system),
        ("light", .light),
        ("dark", .dark),
        ("unknown", .system),
    ] as [(String?, AppearancePreference)])
    func appearanceReadsStoredValue(stored: String?, expected: AppearancePreference) {
        #expect(AppearancePreference(storedValue: stored) == expected)
    }
}
