// swift-tools-version: 6.0

import Foundation
import PackageDescription

// A macOS command-line tool that plays the PC on the Fenpo BLE channel, so device tests S4 and S6
// can run without the Windows app (docs/ios/TESTING.md). Run it on a Mac:
//
//   ios/scripts/generate-proto.sh
//   swift run --package-path ios/Tools/FenpoTestPeripheral

// Embedded Info.plist so macOS can show a Bluetooth permission prompt for the tool.
let infoPlist = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Info.plist").path

let package = Package(
    name: "FenpoTestPeripheral",
    platforms: [
        .macOS(.v15),
    ],
    dependencies: [
        .package(path: "../../FenpoKit"),
    ],
    targets: [
        .executableTarget(
            name: "FenpoTestPeripheral",
            dependencies: [
                .product(name: "FenpoProtocol", package: "FenpoKit"),
                .product(name: "FenpoCore", package: "FenpoKit"),
                .product(name: "FenpoApple", package: "FenpoKit"),
            ],
            linkerSettings: [
                .unsafeFlags(["-Xlinker", "-sectcreate", "-Xlinker", "__TEXT", "-Xlinker", "__info_plist", "-Xlinker", infoPlist]),
            ]
        ),
    ]
)
