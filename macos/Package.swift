// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "InputBeacon",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "InputBeacon", targets: ["InputBeacon"])],
    targets: [
        .target(name: "BeaconCore"),
        .executableTarget(name: "InputBeacon", dependencies: ["BeaconCore"]),
        .testTarget(name: "BeaconCoreTests", dependencies: ["BeaconCore"])
    ]
)
