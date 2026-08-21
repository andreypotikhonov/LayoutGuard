// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "LayoutGuard",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "LayoutGuard", targets: ["LayoutGuard"])
    ],
    targets: [
        .executableTarget(
            name: "LayoutGuard",
            path: "Sources/LayoutGuard",
            swiftSettings: [.swiftLanguageMode(.v5)]
        )
    ]
)
