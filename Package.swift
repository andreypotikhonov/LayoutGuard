// swift-tools-version: 6.0

import Foundation
import PackageDescription

let packageDirectory = URL(fileURLWithPath: #filePath).deletingLastPathComponent().path
let hunspellLibraryDirectory = "\(packageDirectory)/Vendor/Hunspell/lib"

let package = Package(
    name: "LayoutGuard",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "LayoutGuard", targets: ["LayoutGuard"])
    ],
    targets: [
        .systemLibrary(
            name: "CHunspell",
            path: "Sources/CHunspell"
        ),
        .executableTarget(
            name: "LayoutGuard",
            dependencies: ["CHunspell"],
            path: "Sources/LayoutGuard",
            swiftSettings: [.swiftLanguageMode(.v5)],
            linkerSettings: [
                .unsafeFlags(["-L", hunspellLibraryDirectory]),
                .linkedLibrary("c++")
            ]
        )
    ]
)
