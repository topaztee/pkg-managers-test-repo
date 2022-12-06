# Prevents integrating the generated pod targets from
# the Pods/Pods.xcodeproj and its artifacts into the
# shared Example.xcodeproj
install! 'cocoapods', :integrate_targets => false

# Instantiates the pods to a specific platform.
platform :ios, '12.4'

# Instructs CocoaPods to use the shared Example project.
# project '../exampleProject/Example.xcodeproj'

# Describes the dependencies for the app target "Example".
target 'Example' do
    pod 'sqlite3', '3.25.3', inhibit_warnings: true
    pod 'sqlite3/fts'
    pod 'sqlite3/fts5'
    pod 'expat', '2.1'
    pod 'FFmpeg', '2.8.3'
    pod 'freetype', '2.6.4'
    pod 'ImageMagick', '6.8.8-9'
    pod 'Mosquitto', '1.4.8'
    pod 'OpenSSL', '1.0.0'
    pod 'zziplib', '0.13.62'
end
