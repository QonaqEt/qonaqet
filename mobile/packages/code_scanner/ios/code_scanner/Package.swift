// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "code_scanner",
    platforms: [
        .iOS("15.0")
    ],
    products: [
        .library(name: "code-scanner", targets: ["code_scanner"])
    ],
    dependencies: [
        .package(name: "FlutterFramework", path: "../FlutterFramework")
    ],
    targets: [
        .target(
            name: "code_scanner",
            dependencies: [
                .product(name: "FlutterFramework", package: "FlutterFramework")
            ],
            resources: [
                .process("PrivacyInfo.xcprivacy"),
            ]
        )
    ]
)
