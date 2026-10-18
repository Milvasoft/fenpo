import FenpoCore
import Testing

struct PCBluetoothNameTests {
    @Test(arguments: [
        ("BUGRAKOSEN", "BUGRAKOSEN"),
        ("BugraKosen", "BUGRAKOSEN"),
        ("DESKTOP-OFFICE-WORKSTATION", "DESKTOP-OFFICE-"),
        ("  BugraKosen ", "BUGRAKOSEN"),
        ("", "BUGRAKOSEN"),
        ("BUGRAKOSEN", ""),
    ])
    func matchesTheSamePC(bluetoothName: String, pairedName: String) {
        #expect(PCBluetoothName.matches(bluetoothName: bluetoothName, pairedName: pairedName))
    }

    @Test(arguments: [
        ("SENAKOSEN", "BUGRAKOSEN"),
        ("DESKTOP-A1B2C3D", "DESKTOP-X9Y8Z7W"),
        ("Living room PC", "BUGRAKOSEN"),
    ])
    func rejectsAnotherPC(bluetoothName: String, pairedName: String) {
        #expect(!PCBluetoothName.matches(bluetoothName: bluetoothName, pairedName: pairedName))
    }
}
