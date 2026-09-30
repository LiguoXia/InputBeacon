import XCTest
@testable import BeaconCore

final class ThirdPartyInputTests: XCTestCase {
    let sogou = InputQueryContext(provider: .sogou, sourceID: "com.sogou.inputmethod.sogou.pinyin", applicationPID: 10)
    func testProviderIdentificationDoesNotMatchLookalikeIDs() {
        XCTAssertEqual(ThirdPartyInputProvider.identify(sourceID: sogou.sourceID), .sogou)
        XCTAssertEqual(ThirdPartyInputProvider.identify(sourceID: "custom", bundleID: "com.sogou.inputmethod.sogou"), .sogou)
        XCTAssertEqual(ThirdPartyInputProvider.identify(sourceID: "im.rime.inputmethod.Squirrel.Rime"), .squirrel)
        XCTAssertNil(ThirdPartyInputProvider.identify(sourceID: "not.com.sogou.inputmethod.sogou"))
        XCTAssertNil(ThirdPartyInputProvider.identify(sourceID: "com.sogou.inputmethod.sogoufake"))
    }
    func testSogouWireStates() {
        for (value, expected) in [(0, InputMode.chinese), (1, .english), (2, .english), (3, .english)] {
            XCTAssertEqual(ThirdPartyInputProvider.sogou.decode(object: nil, userInfo: ["inputStatus": String(value)]), expected)
            XCTAssertEqual(ThirdPartyInputProvider.sogou.decode(object: nil, userInfo: ["inputStatus": NSNumber(value: value)]), expected)
        }
    }
    func testSogouRejectsMalformedOrUnknownState() {
        for value: Any in ["", "中文", "4", "-1", "1.0", " 1", true, false, 0.5, 4, NSNull(), ["0"]] {
            XCTAssertNil(ThirdPartyInputProvider.sogou.decode(object: nil, userInfo: ["inputStatus": value]))
        }
        XCTAssertNil(ThirdPartyInputProvider.sogou.decode(object: "0", userInfo: nil))
        XCTAssertNil(ThirdPartyInputProvider.sogou.decode(object: nil, userInfo: ["qbanStatus": "0"]))
    }
    func testSquirrelWireStates() {
        XCTAssertEqual(ThirdPartyInputProvider.squirrel.decode(object: "ascii", userInfo: nil), .english)
        XCTAssertEqual(ThirdPartyInputProvider.squirrel.decode(object: "nascii", userInfo: nil), .chinese)
        for value: Any in ["", "ASCII", "unknown", true, 0] {
            XCTAssertNil(ThirdPartyInputProvider.squirrel.decode(object: value, userInfo: nil))
        }
    }
    func testMissingLanguageMetadataDoesNotHideKnownSource() {
        XCTAssertEqual(InputSourceClassifier.classify(sourceID: sogou.sourceID, bundleID: "", languages: []), .chinese)
        XCTAssertEqual(InputSourceClassifier.classify(sourceID: "com.apple.keylayout.ABC", bundleID: "", languages: []), .english)
        XCTAssertEqual(InputSourceClassifier.classify(sourceID: "custom", bundleID: "", languages: ["", "zh-Hans"]), .chinese)
        XCTAssertEqual(InputSourceClassifier.classify(sourceID: "custom", bundleID: "", languages: ["ja"]), .other)
        XCTAssertEqual(InputSourceClassifier.classify(sourceID: "custom", bundleID: "", languages: []), .unknown)
    }
    func testNoModeBeforeResponseAndNoConcurrentRequests() {
        var query = InputModeQuery(); query.update(context: sogou)
        XCTAssertNil(query.mode(now: 0))
        XCTAssertEqual(query.request(now: 0), sogou)
        XCTAssertNil(query.request(now: 0.1))
        XCTAssertTrue(query.receive(provider: .sogou, mode: .english, now: 0.15))
        XCTAssertEqual(query.mode(now: 0.15), .english)
        XCTAssertNil(query.request(now: 0.19))
        XCTAssertEqual(query.request(now: 0.21), sogou)
    }
    func testSameSourceIDChangesInternalMode() {
        var query = InputModeQuery(); query.update(context: sogou)
        _ = query.request(now: 0); query.receive(provider: .sogou, mode: .chinese, now: 0.01)
        XCTAssertEqual(query.mode(now: 0.1), .chinese)
        _ = query.request(now: 0.2); query.receive(provider: .sogou, mode: .english, now: 0.21)
        XCTAssertEqual(query.mode(now: 0.3), .english)
        _ = query.request(now: 0.4); query.receive(provider: .sogou, mode: .chinese, now: 0.41)
        XCTAssertEqual(query.mode(now: 0.5), .chinese)
    }
    func testAppSwitchDiscardsOldReplyBeforeRequestingNewState() {
        var query = InputModeQuery(); query.update(context: sogou); _ = query.request(now: 0)
        let other = InputQueryContext(provider: .sogou, sourceID: sogou.sourceID, applicationPID: 20)
        query.update(context: other)
        XCTAssertNil(query.request(now: 0.1))
        XCTAssertFalse(query.receive(provider: .sogou, mode: .english, now: 0.15))
        XCTAssertNil(query.mode(now: 0.15))
        XCTAssertEqual(query.request(now: 0.2), other)
        XCTAssertTrue(query.receive(provider: .sogou, mode: .chinese, now: 0.21))
    }
    func testSourceSwitchClearsCachedMode() {
        var query = InputModeQuery(); query.update(context: sogou); _ = query.request(now: 0)
        query.receive(provider: .sogou, mode: .english, now: 0.01)
        query.update(context: nil)
        XCTAssertNil(query.mode(now: 0.02)); XCTAssertNil(query.request(now: 0.2))
        query.update(context: sogou)
        XCTAssertNil(query.mode(now: 0.3))
    }
    func testNoUnsolicitedOrWrongProviderReplies() {
        var query = InputModeQuery(); query.update(context: sogou)
        XCTAssertFalse(query.receive(provider: .sogou, mode: .english, now: 0))
        _ = query.request(now: 0)
        XCTAssertFalse(query.receive(provider: .squirrel, mode: .english, now: 0.1))
        XCTAssertTrue(query.receive(provider: .sogou, mode: .chinese, now: 0.2))
    }
    func testTimeoutInvalidatesAndBacksOff() {
        var query = InputModeQuery(); query.update(context: sogou); _ = query.request(now: 0)
        query.receive(provider: .sogou, mode: .chinese, now: 0.01)
        _ = query.request(now: 0.2)
        XCTAssertNil(query.request(now: 0.71)); XCTAssertNil(query.mode(now: 0.71))
        XCTAssertEqual(query.diagnostic, "timeout")
        XCTAssertFalse(query.receive(provider: .sogou, mode: .english, now: 0.8))
        XCTAssertNil(query.request(now: 0.9))
        XCTAssertEqual(query.request(now: 1), sogou)
    }
    func testSampleExpiresEvenWithoutFurtherPolling() {
        var query = InputModeQuery(); query.update(context: sogou); _ = query.request(now: 0)
        query.receive(provider: .sogou, mode: .chinese, now: 0.01)
        XCTAssertNil(query.mode(now: 1))
        XCTAssertNil(query.mode(now: -1))
    }
    func testMalformedReplyClearsPreviouslyConfirmedMode() {
        var query = InputModeQuery(); query.update(context: sogou); _ = query.request(now: 0)
        query.receive(provider: .sogou, mode: .chinese, now: 0.01)
        _ = query.request(now: 0.2)
        XCTAssertFalse(query.receive(provider: .sogou, mode: nil, now: 0.21))
        XCTAssertNil(query.mode(now: 0.3)); XCTAssertEqual(query.diagnostic, "unsupported-reply")
    }
    func testInternalModeChangeTriggersFollowWithUnchangedSourceID() {
        var lifetime = FollowLifetime()
        var state = InputState(mode: .chinese, sourceID: sogou.sourceID)
        state.modeOrigin = "sogou-status"
        lifetime.observe(state: state, field: 1, seconds: 1, now: 0)
        state.mode = .english
        lifetime.observe(state: state, field: 1, seconds: 1, now: 2)
        lifetime.observe(state: state, field: 1, seconds: 1, now: 2.21)
        XCTAssertEqual(lifetime.triggerCount, 2)
        XCTAssertEqual(lifetime.lastTrigger, "Input mode confirmed")
    }
}
