#!/bin/bash
set -euo pipefail
[[ "${CI:-}" == "true" ]] || { echo 'This test requires a disposable CI account'; exit 1; }
cd "$(dirname "$0")/.."
swift build
export INPUTBEACON_PROBE="$(swift build --show-bin-path)/InputBeacon"
mkdir -p .build/sogou-test
cd .build/sogou-test
# Official vendor distribution, pinned to the inspected 6.25.1.11973 package.
curl --fail --location --retry 2 --max-time 180 https://ime.gtimg.com/pc/sogou_mac_625a.zip -o sogou.zip
echo '62b265988e0e3cae590ebf8c4588945011987f606687335ef29edf6c622522c6  sogou.zip' | shasum -a 256 -c -
ditto -x -k sogou.zip .
ditto -x -k sogou_mac_625a.app/Contents/Resources/SogouInput.zip payload
sudo ditto payload/SogouInput.app '/Library/Input Methods/SogouInput.app'
sudo xattr -dr com.apple.quarantine '/Library/Input Methods/SogouInput.app'
open '/Library/Input Methods/SogouInput.app'
if [[ "${INPUTBEACON_FULL_IME_TEST:-}" != "true" ]]; then
  swiftc ../../Sources/BeaconCore/InputState.swift ../../Sources/BeaconCore/ThirdPartyInput.swift \
    ../../scripts/SogouQueryCheck.swift -o SogouQueryCheck
  ./SogouQueryCheck
  exit 0
fi
# Optional full-session test for a runner where the installed IME is selectable.
mkdir -p SogouTest.app/Contents/MacOS
cat > SogouTest.app/Contents/Info.plist <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.liguoxia.InputBeacon.SogouTest</string>
<key>CFBundleExecutable</key><string>SogouTest</string>
<key>CFBundleName</key><string>InputBeacon Sogou test</string>
<key>CFBundlePackageType</key><string>APPL</string>
</dict></plist>
PLIST
swiftc ../../Sources/BeaconCore/InputState.swift ../../Sources/BeaconCore/ThirdPartyInput.swift \
  ../../scripts/SogouIntegration.swift -o SogouTest.app/Contents/MacOS/SogouTest
codesign --force --sign - SogouTest.app
open -W -n --stdout "$PWD/integration.log" --stderr "$PWD/integration-errors.log" \
  --env CI=true --env "INPUTBEACON_PROBE=$INPUTBEACON_PROBE" \
  --env "INPUTBEACON_TEST_RESULT=$PWD/result.txt" SogouTest.app
cat integration.log
test -f result.txt && test "$(cat result.txt)" = PASS
