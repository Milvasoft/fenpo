// swift-tools-version: 6.0

import PackageDescription

// FenpoKit is split in two:
// - FenpoProtocol and FenpoCore have no Apple framework dependencies and also build
//   and test with the Swift toolchain on Windows.
// - FenpoApple holds everything that needs CoreBluetooth, Network.framework,
//   CryptoKit, Security or SwiftUI. It only exists when building on an Apple host.

var products: [Product] = [
    .library(name: "FenpoProtocol", targets: ["FenpoProtocol"]),
    .library(name: "FenpoCore", targets: ["FenpoCore"]),
]

var targets: [Target] = [
    // Swift types generated from protocol/fenpo/v1/*.proto by ios/scripts/generate-proto.sh.
    .target(
        name: "FenpoProtocol",
        dependencies: [.product(name: "SwiftProtobuf", package: "swift-protobuf")]
    ),
    .target(
        name: "FenpoCore",
        dependencies: ["FenpoProtocol"]
    ),
    .testTarget(
        name: "FenpoCoreTests",
        dependencies: ["FenpoCore", "FenpoProtocol"]
    ),
]

#if canImport(Darwin)
products.append(.library(name: "FenpoApple", targets: ["FenpoApple"]))
targets.append(
    .target(
        name: "FenpoApple",
        dependencies: ["FenpoCore", "FenpoProtocol"]
    )
)
targets.append(
    .testTarget(
        name: "FenpoAppleTests",
        dependencies: ["FenpoApple", "FenpoCore", "FenpoProtocol"]
    )
)
#endif

let package = Package(
    name: "FenpoKit",
    platforms: [
        .iOS(.v18),
        .macOS(.v15),
    ],
    products: products,
    dependencies: [
        .package(url: "https://github.com/apple/swift-protobuf.git", from: "1.28.0"),
    ],
    targets: targets
)
