import XCTest
@testable import BeaconCore

final class BeaconCoreTests: XCTestCase {
    let english = InputState(mode: .english, sourceID: "en")
    let chinese = InputState(mode: .chinese, sourceID: "zh")

    func testLanguageClassification() {
        XCTAssertEqual(InputState.classify(language: "zh-Hans"), .chinese)
        XCTAssertEqual(InputState.classify(language: "ZH_TW"), .chinese)
        XCTAssertEqual(InputState.classify(language: "en-US"), .english)
        XCTAssertEqual(InputState.classify(language: "ja"), .other)
        XCTAssertEqual(InputState.classify(language: nil), .unknown)
        XCTAssertEqual(InputState.classify(language: ""), .unknown)
    }
    func testEffectiveCase() {
        for caps in [false, true] {
            for shift in [false, true] {
                XCTAssertEqual(InputState(caps: caps, shift: shift).uppercase, caps != shift)
            }
        }
    }
    func show(_ life: FollowLifetime, now: Double, seconds: Int = 3, caret: Bool = true, known: Bool = true) -> Bool {
        life.shouldShow(enabled: true, seconds: seconds, hasCaret: caret, knownState: known, now: now)
    }
    func testFocusImmediatelyShowsFullDuration() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 3, now: 10)
        XCTAssertTrue(show(life, now: 12.999))
        XCTAssertFalse(show(life, now: 13))
        XCTAssertEqual(life.lastTrigger, "Input field focused")
    }
    func testMovementDoesNotExtendExpiry() {
        var life = FollowLifetime()
        for i in 0...40 { life.observe(state: english, field: 1, seconds: 3, now: Double(i) / 10) }
        XCTAssertEqual(life.triggerCount, 1)
        XCTAssertFalse(show(life, now: 4))
    }
    func testSwitchFieldAndReturnRetrigger() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 3, now: 0)
        life.observe(state: english, field: 2, seconds: 3, now: 4)
        life.observe(state: english, field: 1, seconds: 3, now: 8)
        XCTAssertEqual(life.triggerCount, 3)
        XCTAssertTrue(show(life, now: 10.9))
    }
    func testMissingCaretDoesNotRetriggerSameField() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 1, now: 0)
        life.observe(state: english, field: nil, seconds: 1, now: 1.1)
        life.observe(state: english, field: 1, seconds: 1, now: 1.2)
        XCTAssertEqual(life.triggerCount, 1)
        XCTAssertFalse(show(life, now: 1.2, seconds: 1))
    }
    func testUnknownInitialStateWaitsForKnownCaret() {
        var life = FollowLifetime()
        life.observe(state: InputState(), field: 1, seconds: 3, now: 0)
        XCTAssertEqual(life.triggerCount, 0)
        life.observe(state: english, field: 1, seconds: 3, now: 5)
        XCTAssertEqual(life.triggerCount, 1)
        XCTAssertTrue(show(life, now: 7.9))
    }
    func testUnknownRecoveryDoesNotRetrigger() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 1, now: 0)
        life.observe(state: InputState(), field: 1, seconds: 1, now: 1.1)
        life.observe(state: english, field: 1, seconds: 1, now: 1.2)
        XCTAssertEqual(life.triggerCount, 1)
    }
    func testModeMustRemainStableFor200ms() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 1, now: 0)
        life.observe(state: chinese, field: 1, seconds: 1, now: 2)
        life.observe(state: chinese, field: 1, seconds: 1, now: 2.1)
        XCTAssertEqual(life.triggerCount, 1)
        life.observe(state: chinese, field: 1, seconds: 1, now: 2.21)
        XCTAssertEqual(life.triggerCount, 2)
        XCTAssertTrue(show(life, now: 3.2, seconds: 1))
    }
    func testBriefModeFluctuationIsIgnored() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 1, now: 0)
        life.observe(state: chinese, field: 1, seconds: 1, now: 2)
        life.observe(state: english, field: 1, seconds: 1, now: 2.1)
        XCTAssertEqual(life.triggerCount, 1)
    }
    func testPollingPauseDoesNotConfirmMode() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 1, now: 0)
        life.observe(state: chinese, field: 1, seconds: 1, now: 2)
        life.observe(state: chinese, field: 1, seconds: 1, now: 4)
        XCTAssertEqual(life.triggerCount, 1)
        life.observe(state: chinese, field: 1, seconds: 1, now: 4.21)
        XCTAssertEqual(life.triggerCount, 2)
    }
    func testCaseChangeImmediatelyTriggers() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 1, now: 0)
        var upper = english; upper.caps = true
        life.observe(state: upper, field: 1, seconds: 1, now: 2)
        XCTAssertTrue(show(life, now: 2, seconds: 1))
        XCTAssertEqual(life.lastTrigger, "Letter case changed")
    }
    func testCapsShiftCombinationWithoutCaseChangeDoesNotTrigger() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 1, now: 0)
        var sameCase = english; sameCase.caps = true; sameCase.shift = true
        life.observe(state: sameCase, field: 1, seconds: 1, now: 2)
        XCTAssertEqual(life.triggerCount, 1)
    }
    func testAlwaysVisibleStillRequiresValidKnownCaret() {
        let life = FollowLifetime()
        XCTAssertTrue(show(life, now: 100, seconds: 0))
        XCTAssertFalse(show(life, now: 100, seconds: 0, caret: false))
        XCTAssertFalse(show(life, now: 100, seconds: 0, known: false))
        XCTAssertFalse(life.shouldShow(enabled: false, seconds: 0, hasCaret: true, knownState: true, now: 0))
    }
    func testResetClearsFocusAndExpiry() {
        var life = FollowLifetime()
        life.observe(state: english, field: 1, seconds: 3, now: 0)
        life.reset()
        XCTAssertFalse(show(life, now: 1))
        life.observe(state: english, field: 1, seconds: 3, now: 2)
        XCTAssertTrue(show(life, now: 4.9))
    }
    func testAXCoordinatesOnMainAndUpperDisplays() {
        XCTAssertEqual(BeaconGeometry.appKitRect(fromAX: CGRect(x: 20, y: 100, width: 1, height: 20), mainDisplayHeight: 900), CGRect(x: 20, y: 780, width: 1, height: 20))
        XCTAssertEqual(BeaconGeometry.appKitRect(fromAX: CGRect(x: -900, y: -500, width: 1, height: 20), mainDisplayHeight: 900), CGRect(x: -900, y: 1380, width: 1, height: 20))
    }
    func testBubbleAvoidsTopAndRightEdges() {
        let area = CGRect(x: 0, y: 0, width: 1000, height: 800)
        let frame = BeaconGeometry.bubble(caret: CGRect(x: 990, y: 780, width: 1, height: 18), size: CGSize(width: 84, height: 36), visibleFrame: area)
        XCTAssertTrue(area.contains(frame))
        XCTAssertLessThan(frame.maxX, 990)
        XCTAssertLessThan(frame.maxY, 780)
    }
    func testNegativeOriginMonitorClamping() {
        let area = CGRect(x: -1920, y: -200, width: 1920, height: 1080)
        let frame = BeaconGeometry.clamp(CGRect(x: -2100, y: 2000, width: 84, height: 36), to: area)
        XCTAssertEqual(frame.minX, -1920)
        XCTAssertEqual(frame.maxY, 880)
    }
}
