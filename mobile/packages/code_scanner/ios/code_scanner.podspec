Pod::Spec.new do |s|
  s.name             = 'code_scanner'
  s.version          = '1.0.0'
  s.summary          = 'QONAQET: QR skaneri (VisionKit DataScanner) — öz plugin-imiz.'
  s.description      = 'VisionKit DataScannerViewController ilə QR oxuma; kamera icazəsi idarəsi.'
  s.homepage         = 'https://qonaqet.az'
  s.license          = { :type => 'Proprietary', :text => 'QONAQET' }
  s.author           = { 'QONAQET' => 'dev@qonaqet.az' }
  s.source           = { :path => '.' }
  s.source_files     = 'code_scanner/Sources/code_scanner/**/*.swift'
  s.resource_bundles = { 'code_scanner_privacy' => ['code_scanner/Sources/code_scanner/PrivacyInfo.xcprivacy'] }
  s.dependency 'Flutter'
  s.frameworks = 'VisionKit', 'AVFoundation'
  s.platform = :ios, '15.0'
  s.pod_target_xcconfig = { 'DEFINES_MODULE' => 'YES', 'EXCLUDED_ARCHS[sdk=iphonesimulator*]' => 'i386' }
  s.swift_version = '5.0'
end
