Pod::Spec.new do |s|
  s.name             = 'secure_store'
  s.version          = '1.0.0'
  s.summary          = 'QONAQET: tokenləri iOS Keychain-də saxlayan öz plugin-imiz.'
  s.description      = 'Keychain (kSecClassGenericPassword) üzərində read/write/delete.'
  s.homepage         = 'https://qonaqet.az'
  s.license          = { :type => 'Proprietary', :text => 'QONAQET' }
  s.author           = { 'QONAQET' => 'dev@qonaqet.az' }
  s.source           = { :path => '.' }
  s.source_files     = 'secure_store/Sources/secure_store/**/*.swift'
  s.resource_bundles = { 'secure_store_privacy' => ['secure_store/Sources/secure_store/PrivacyInfo.xcprivacy'] }
  s.dependency 'Flutter'
  s.platform = :ios, '15.0'
  s.pod_target_xcconfig = { 'DEFINES_MODULE' => 'YES', 'EXCLUDED_ARCHS[sdk=iphonesimulator*]' => 'i386' }
  s.swift_version = '5.0'
end
