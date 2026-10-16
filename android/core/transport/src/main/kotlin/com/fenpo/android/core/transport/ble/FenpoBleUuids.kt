package com.fenpo.android.core.transport.ble

import java.util.UUID

/**
 * Fixed UUIDs of the Fenpo BLE channel (③). The same on every PC so AccessorySetupKit and
 * CompanionDeviceManager can filter by them (`protocol: fixed Fenpo BLE service and
 * characteristic UUIDs`, beede75; values as in spikes/S4-ble-peripheral).
 */
public object FenpoBleUuids {
    /** Primary service, advertised by the PC. */
    public val SERVICE: UUID = UUID.fromString("1888047E-C8C5-45CC-AAEA-333A8859E79F")

    /** Phone → PC, write without response, encryption required. */
    public val RX: UUID = UUID.fromString("ECC8D0D1-990A-4B02-A4FC-49FA647E7254")

    /** PC → phone, notify, encryption required. */
    public val TX: UUID = UUID.fromString("E9EC01E0-8C02-4B8E-902C-4F113E25DFC8")

    /** Client Characteristic Configuration descriptor, written to turn on `tx` notifications. */
    public val CCCD: UUID = UUID.fromString("00002902-0000-1000-8000-00805F9B34FB")
}
