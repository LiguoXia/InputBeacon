import XCTest
import AppKit
import BeaconCore
@testable import BeaconMac

final class InputProbeTests: XCTestCase {
    let sogou = InputSourceMetadata(id: "com.sogou.inputmethod.sogou.pinyin", bundleID: "com.sogou.inputmethod.sogou", name: "搜狗", languages: [])
    func spin(_ seconds: Double) {
        let deadline = Date().addingTimeInterval(seconds)
        while Date() < deadline { RunLoop.main.run(until: Date().addingTimeInterval(0.005)) }
    }
    func send(_ raw: String) {
        DistributedNotificationCenter.default().postNotificationName(.init(ThirdPartyInputProvider.sogou.responseName),
                                                                     object: nil, userInfo: ["inputStatus": raw], deliverImmediately: true)
        spin(0.05)
    }
    func testProductionReceiverFollowsInternalModeWithUnchangedSource() {
        let probe = InputProbe(sourceReader: { self.sogou }, foregroundReader: { 12345 })
        XCTAssertEqual(probe.read().mode, .unknown)
        send("0")
        XCTAssertEqual(probe.read().mode, .chinese)
        XCTAssertEqual(probe.read().modeOrigin, "sogou-status")
        spin(0.21); _ = probe.read(); send("1")
        XCTAssertEqual(probe.read().mode, .english)
        spin(0.21); _ = probe.read(); send("0")
        XCTAssertEqual(probe.read().mode, .chinese)
        XCTAssertEqual(probe.replyCount, 3)
    }
    func testProductionReceiverRejectsReplyAfterForegroundChanges() {
        var pid: pid_t = 12345
        let probe = InputProbe(sourceReader: { self.sogou }, foregroundReader: { pid })
        _ = probe.read(); pid = 23456; send("1")
        XCTAssertEqual(probe.read().mode, .unknown)
        XCTAssertEqual(probe.replyCount, 0)
        send("0")
        XCTAssertEqual(probe.read().mode, .chinese)
    }
    func testProductionReceiverDoesNotApplySogouModeToSystemSource() {
        var source = sogou
        let probe = InputProbe(sourceReader: { source }, foregroundReader: { 12345 })
        _ = probe.read()
        source = InputSourceMetadata(id: "com.apple.keylayout.ABC", languages: ["en"])
        send("0")
        XCTAssertEqual(probe.read().mode, .english)
        XCTAssertEqual(probe.read().modeOrigin, "system-input-source")
        XCTAssertEqual(probe.replyCount, 0)
    }
    func testProductionReceiverTreatsMissingReplyAsUnknownNotChinese() {
        let probe = InputProbe(sourceReader: { self.sogou }, foregroundReader: { 12345 })
        _ = probe.read(); spin(0.55)
        XCTAssertEqual(probe.read().mode, .unknown)
        XCTAssertTrue(probe.diagnostic.contains("ThirdPartyQuery=timeout"))
    }
}
