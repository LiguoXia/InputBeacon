import AppKit
import Carbon
import BeaconCore

struct InputSourceMetadata {
    var id = ""
    var bundleID = ""
    var name = "未知输入源"
    var languages: [String] = []
    var modeID = ""

    static func current() -> Self {
        guard let unmanaged = TISCopyCurrentKeyboardInputSource() else { return Self() }
        let source = unmanaged.takeRetainedValue()
        func property<T>(_ key: CFString, as: T.Type) -> T? {
            guard let pointer = TISGetInputSourceProperty(source, key) else { return nil }
            return Unmanaged<AnyObject>.fromOpaque(pointer).takeUnretainedValue() as? T
        }
        return Self(id: property(kTISPropertyInputSourceID, as: String.self) ?? "",
                    bundleID: property(kTISPropertyBundleID, as: String.self) ?? "",
                    name: property(kTISPropertyLocalizedName, as: String.self) ?? "未知输入源",
                    languages: property(kTISPropertyInputSourceLanguages, as: [String].self) ?? [],
                    modeID: property(kTISPropertyInputModeID, as: String.self) ?? "")
    }
    func queryContext(frontmostPID: pid_t?) -> InputQueryContext? {
        guard let provider = ThirdPartyInputProvider.identify(sourceID: id, bundleID: bundleID),
              let pid = frontmostPID,
              pid != ProcessInfo.processInfo.processIdentifier else { return nil }
        return InputQueryContext(provider: provider, sourceID: id, applicationPID: pid)
    }
}

public final class InputProbe: NSObject {
    private var query = InputModeQuery()
    private var metadata = InputSourceMetadata()
    public private(set) var replyCount = 0
    private var observers: [NSObjectProtocol] = []
    private let sourceReader: () -> InputSourceMetadata
    private let foregroundReader: () -> pid_t?

    public override convenience init() {
        self.init(sourceReader: InputSourceMetadata.current,
                  foregroundReader: { NSWorkspace.shared.frontmostApplication?.processIdentifier })
    }
    // Injectable snapshots let tests exercise the production notification receiver
    // without changing the user's input source or requiring third-party activation.
    init(sourceReader: @escaping () -> InputSourceMetadata, foregroundReader: @escaping () -> pid_t?) {
        self.sourceReader = sourceReader; self.foregroundReader = foregroundReader
        super.init()
        let center = DistributedNotificationCenter.default()
        for provider in [ThirdPartyInputProvider.sogou, .squirrel] {
            center.addObserver(self, selector: #selector(receive(_:)), name: .init(provider.responseName),
                               object: nil, suspensionBehavior: .deliverImmediately)
        }
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didActivateApplicationNotification,
                                                                           object: nil, queue: .main) { [weak self] _ in
            self?.refreshContext()
        })
        center.addObserver(self, selector: #selector(sourceChanged(_:)),
                           name: .init(kTISNotifySelectedKeyboardInputSourceChanged as String), object: nil,
                           suspensionBehavior: .deliverImmediately)
    }
    deinit {
        DistributedNotificationCenter.default().removeObserver(self)
        for observer in observers { NSWorkspace.shared.notificationCenter.removeObserver(observer) }
    }
    private func refreshContext() {
        metadata = sourceReader()
        query.update(context: metadata.queryContext(frontmostPID: foregroundReader()))
    }
    @objc private func sourceChanged(_ notification: Notification) { refreshContext() }
    @objc private func receive(_ notification: Notification) {
        // Re-read actual foreground/source before accepting an asynchronous response.
        refreshContext()
        guard let provider = [ThirdPartyInputProvider.sogou, .squirrel].first(where: { $0.responseName == notification.name.rawValue }) else { return }
        let mode = provider.decode(object: notification.object, userInfo: notification.userInfo)
        if query.receive(provider: provider, mode: mode, now: ProcessInfo.processInfo.systemUptime) { replyCount += 1 }
    }
    public func read() -> InputState {
        refreshContext()
        let flags = CGEventSource.flagsState(.combinedSessionState)
        let now = ProcessInfo.processInfo.systemUptime
        var state = InputState(sourceID: metadata.id, sourceName: metadata.name,
                               caps: flags.contains(.maskAlphaShift), shift: flags.contains(.maskShift))
        state.mode = InputSourceClassifier.classify(sourceID: metadata.id, bundleID: metadata.bundleID, languages: metadata.languages)
        if let provider = ThirdPartyInputProvider.identify(sourceID: metadata.id, bundleID: metadata.bundleID) {
            if let request = query.request(now: now) {
                DistributedNotificationCenter.default().postNotificationName(.init(request.provider.requestName),
                                                                              object: nil, userInfo: nil, deliverImmediately: true)
            }
            // Unknown until confirmed; never infer the internal mode from the source name.
            state.mode = query.mode(now: now) ?? .unknown
            state.modeOrigin = provider.rawValue + (state.mode == .unknown ? "-waiting" : "-status")
        }
        return state
    }
    public var diagnostic: String {
        "InputSource=\(metadata.id)\nInputBundle=\(metadata.bundleID)\nInputModeID=\(metadata.modeID)\nInputLanguages=\(metadata.languages.joined(separator: ","))\nThirdPartyQuery=\(query.diagnostic)\nThirdPartyReplies=\(replyCount)"
    }
}
