// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "secure_store",
    platforms: [
        .iOS("15.0")
    ],
    products: [
        .library(name: "secure-store", targets: ["secure_store"])
    ],
    dependencies: [
        .package(name: "FlutterFramework", path: "../FlutterFramework")
    ],
    targets: [
        .target(
            name: "secure_store",
            dependencies: [
                .product(name: "FlutterFramework", package: "FlutterFramework")
            ],
            resources: [
                .process("PrivacyInfo.xcprivacy"),
            ]
        )
    ]
)
