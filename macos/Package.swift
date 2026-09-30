// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "InputBeacon",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "InputBeacon", targets: ["InputBeacon"])],
    targets: [
        .target(name: "BeaconCore"),
        .target(name: "BeaconMac", dependencies: ["BeaconCore"]),
        .executableTarget(name: "InputBeacon", dependencies: ["BeaconCore", "BeaconMac"]),
        .testTarget(name: "BeaconCoreTests", dependencies: ["BeaconCore"]),
        .testTarget(name: "BeaconMacTests", dependencies: ["BeaconMac", "BeaconCore"])
    ]
)
