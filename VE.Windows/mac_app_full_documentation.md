# VE AI — macOS Desktop App: Complete Technical Documentation

---

## STEP 1 — Project Overview

### App Name
**VE AI** (Bundle ID: `com.veai.VEAI`)

### Purpose
A native macOS **menu bar assistant app** powered by AI prediction and voice dictation. Users hold modifier keys (Control, Option, Fn) for intelligent text predictions based on screen context, speech-to-text dictation, and voice instructions. Text is automatically pasted into the active application. The app also provides an AI chat interface, meeting transcription/notes, calendar integration, and a knowledge base.

### Platform
- **Language:** Swift 5
- **UI Framework:** SwiftUI + AppKit hybrid (menu bar app with floating NSPanel windows)
- **Minimum OS:** macOS 14 Sonoma+
- **Architecture:** arm64 + x86_64

### High-Level Architecture
```
┌─────────────────────────────────────────────────────────────────┐
│                       DynamicNotchApp (@main)                   │
│  MenuBarExtra + AppDelegate + Sparkle Updater + Sentry          │
├─────────────────────────────────────────────────────────────────┤
│  ContentView (Notch overlay — floating transparent window)      │
│  ├── ClosedNotchContentView (idle/prediction/dictation states)  │
│  └── NotchHomeView (expanded: chat, auth, meeting, settings)    │
├─────────────────────────────────────────────────────────────────┤
│  FloatingWindowController (floating always-on-top chat panel)   │
│  SettingsWindowController (main app window w/ sidebar nav)      │
├─────────────────────────────────────────────────────────────────┤
│  Managers                           │  Services                 │
│  ├── AuthManager (OAuth+JWT)        │  ├── BaseURLService       │
│  ├── KeyboardMonitor               │  ├── DictationService      │
│  ├── LowLevelKeyTap (CGEventTap)   │  ├── UnifiedAudioService   │
│  ├── NetworkService                │  ├── MeetingService        │
│  ├── ChatManager                   │  ├── TokenRefreshService   │
│  └── WebSocketRegistry             │  ├── ErrorService (Sentry) │
│                                     │  ├── HomeService           │
│                                     │  └── PredictionFeedback    │
├─────────────────────────────────────────────────────────────────┤
│  WebSocket Clients                                              │
│  ├── UnifiedAudioSocketClient (cursor-intelligence)             │
│  ├── VoiceToTextSocketClient (voice-intelligence)               │
│  ├── MultiAgentSocketClient (ai/chat)                           │
│  └── MeetingSocketClient (recall)                               │
├─────────────────────────────────────────────────────────────────┤
│  XPC Helper (VEAIXPCHelper) — screen reading, accessibility     │
└─────────────────────────────────────────────────────────────────┘
```

### Main Entry Point
**`VE/VEAIApp.swift`** — `@main struct DynamicNotchApp: App`

Initializes: FileLogger, PredictionFeedbackService, Sparkle updater (auto-update), LaunchAtLogin, Sentry crash reporting, and the MenuBarExtra scene.

---

## STEP 2 — File & Folder Structure

```
ve-macos-desktop-app/
├── VE/                                    # Main app target
│   ├── VEAIApp.swift                      # @main entry point, MenuBarExtra scene
│   ├── VEAIApp+Extensions.swift           # App extensions (NoSkipUserDriver, SharedUpdateDelegate)
│   ├── AppDelegate.swift                  # NSApplicationDelegate — properties, terminate
│   ├── AppDelegate+Launch.swift           # applicationDidFinishLaunching, observers, permissions
│   ├── AppDelegate+Notifications.swift    # NSUserNotification handling
│   ├── AppDelegate+PushNotifications.swift# APNs push notification support
│   ├── AppDelegate+RestrictedStates.swift # Waitlist/expired/suspended/no-internet states
│   ├── AppDelegate+URLHandling.swift      # ve:// OAuth callback handling
│   ├── AppDelegate+UpdateChecker.swift    # Sparkle update check scheduling
│   ├── AppDelegate+Windows.swift          # Window creation, positioning, multi-display
│   ├── ContentView.swift                  # Main notch overlay UI (SwiftUI)
│   ├── FloatingWindowController.swift     # Floating chat/instruction panel (NSPanel)
│   ├── VEAIViewCoordinator.swift          # Central state coordinator (singleton)
│   │
│   ├── models/
│   │   ├── AuthState.swift                # AuthState enum (unauthorized/authenticating/authorized/error)
│   │   ├── AuthStorage.swift              # Token storage (Keychain + UserDefaults + Cookies)
│   │   ├── Constants.swift                # All Defaults.Keys (50+ settings)
│   │   ├── VEAIViewModel.swift            # NotchState, sizing, visibility triggers
│   │   ├── ChatModels.swift               # Chat message/conversation models
│   │   ├── HomeModels.swift               # Home/meeting/usage models
│   │   ├── VoiceLogModels.swift           # Voice/prediction log models
│   │   ├── Citation.swift                 # Citation model for chat sources
│   │   └── SharingStateManager.swift      # Prevents notch close during share
│   │
│   ├── managers/
│   │   ├── AuthManager.swift              # OAuth flow, login/logout
│   │   ├── AuthManager+TokenRefresh.swift # Proactive token refresh scheduler
│   │   ├── AuthManager+TenantSettings.swift # Tenant/workspace settings
│   │   ├── AuthManager+UserProfile.swift  # User profile API
│   │   ├── KeyboardMonitor.swift          # Key hold/tap detection, action routing
│   │   ├── LowLevelKeyTap.swift           # CGEventTap for modifier key monitoring
│   │   ├── NetworkService.swift           # HTTP client with auth, CSRF, retry
│   │   ├── ChatManager.swift              # Chat message management
│   │   ├── ClosedNotchViewManager.swift   # Closed notch state management
│   │   ├── EscEventTapManager.swift       # ESC key handling for cancellation
│   │   ├── ImagePreloader.swift           # Remote asset preloading
│   │   ├── ImageService.swift             # Image loading/caching
│   │   ├── AssetDiskCache.swift           # Disk-based asset cache
│   │   ├── RemoteAssetRegistry.swift      # Remote asset URL management
│   │   ├── NotchSpaceManager.swift        # Notch space/display management
│   │   ├── PreviewPanelManager.swift      # Code preview panel management
│   │   ├── PushNotificationManager.swift  # APNs token management
│   │   ├── SleepPreventionManager.swift   # Prevent system sleep during meetings
│   │   └── websocket/
│   │       ├── RetryPolicy.swift          # Exponential backoff with jitter
│   │       ├── WebSocketTransport.swift   # URLSessionWebSocketTask wrapper
│   │       ├── WebSocketRegistry.swift    # Lifecycle coordination for all WS
│   │       └── clients/
│   │           ├── UnifiedAudioSocketClient.swift  # Prediction + dictation WS client
│   │           ├── VoiceToTextSocketClient.swift    # Voice enrollment WS client
│   │           └── MultiAgentSocketClient.swift     # Chat/instruction WS client
│   │
│   ├── services/
│   │   ├── BaseURLService.swift           # Multi-region URL resolution
│   │   ├── TokenRefreshService.swift      # JWT token refresh with retry
│   │   ├── ErrorService.swift             # Sentry + Slack error reporting
│   │   ├── DictationService.swift         # Voice dictation workflow
│   │   ├── UnifiedAudioService.swift      # Prediction audio + screenshot capture
│   │   ├── PredictionFeedbackService.swift# 15s feedback window after prediction
│   │   ├── HomeService.swift              # Dashboard/home data API
│   │   ├── CalendarService.swift          # Calendar events API
│   │   ├── MeetingService.swift           # Meeting lifecycle (start/stop/pause)
│   │   ├── MeetingDetection/
│   │   │   └── MeetingDetectionService.swift # Auto-detect meetings
│   │   ├── ConnectorsService.swift        # Google/Microsoft integrations
│   │   ├── KnowledgeAgentService.swift    # Knowledge base CRUD
│   │   ├── InstructionService.swift       # Instructions API
│   │   ├── MemoryService.swift            # Memory management API
│   │   ├── VoiceService.swift             # Voice log APIs
│   │   ├── VoiceEnrollmentService.swift   # Voice enrollment/profile
│   │   ├── SubscriptionService.swift      # Subscription info API
│   │   ├── TeamMembersService.swift       # Team management API
│   │   ├── WorkspaceService.swift         # Workspace switching API
│   │   ├── DictionaryService.swift        # Custom dictionary API
│   │   ├── SettingsDataService.swift      # Settings data API
│   │   ├── IntercomService.swift          # Intercom help center
│   │   ├── GoogleIntegrationService.swift # Google connector APIs
│   │   ├── IntentMailService.swift        # Intent/email API
│   │   ├── LocalStoreService.swift        # SQLite local data store
│   │   ├── ActivityPipelineService.swift  # Activity capture/sync pipeline
│   │   ├── AppActivityService.swift       # App usage tracking
│   │   ├── ActivityEventMonitor.swift     # System activity events
│   │   ├── AppCategorizer.swift           # App category classification
│   │   ├── VoiceLogsAPIService.swift      # Voice logs API
│   │   ├── VoiceLogsRecordingService.swift# Local voice log recording
│   │   ├── PredictionLogsRecordingService.swift # Prediction log recording
│   │   ├── DictationFeedbackService.swift # Dictation feedback API
│   │   ├── URLSanitizer.swift             # URL sanitization utility
│   │   └── GraphQL/
│   │       ├── GraphQLEndpoint.swift      # GraphQL endpoint config
│   │       ├── GraphqlActions.swift       # GraphQL action definitions
│   │       ├── GraphqlQuery.swift         # GraphQL query builder
│   │       └── GraphqlServices.swift      # GraphQL service layer
│   │
│   ├── components/
│   │   ├── Auth/                          # AuthWrapper, UnauthorizedView
│   │   ├── Chat/                          # ChatView, ChatInputBar, CitationPillView, etc.
│   │   ├── FloatingWindow/               # FloatingBarView, ChatBubble, AudioWaveBars
│   │   ├── Meeting/                       # MeetingView, MeetingSummaryView, MeetingListView
│   │   ├── Notch/                         # ClosedNotchContentView, NotchHomeView, NotchShape
│   │   ├── Onboarding/                    # OnboardingFlowView, WelcomeView, PermissionsRequestView
│   │   ├── Prediction/                    # PredictionView, DictationView, SmoothDotLoader
│   │   ├── Settings/                      # SettingsLayout, MyProfileView, ConnectorsView, etc.
│   │   ├── Tabs/                          # TabButton, TabSelectionView
│   │   ├── Live activities/               # InlineHUD, OpenNotchHUD
│   │   ├── NavigationTabs.swift           # Top-level navigation tabs
│   │   ├── ToastView.swift                # Toast notifications
│   │   ├── HoverButton.swift              # Custom hover button
│   │   ├── LottieView.swift               # Lottie animation wrapper
│   │   └── VESpinner.swift                # Loading spinner
│   │
│   ├── helpers/
│   │   ├── AppHelper.swift                # Window title, app name helpers
│   │   ├── AudioHelper.swift              # AVAudioEngine wrapper
│   │   ├── KeychainService.swift          # Keychain CRUD (Data Protection keychain)
│   │   ├── PasteHelper.swift              # Clipboard paste simulation
│   │   ├── AppleScriptHelper.swift        # AppleScript execution
│   │   ├── CertificatePinning.swift       # SSL certificate pinning
│   │   ├── MeetingAudioStreamer.swift      # Meeting audio capture
│   │   ├── MicrophoneDeviceList.swift     # Audio device enumeration
│   │   ├── SystemIdleHelper.swift         # System idle detection
│   │   ├── ApplicationRelauncher.swift    # App restart
│   │   ├── HelperUtilities.swift          # Timezone, location parsing
│   │   ├── Clipboard+Content.swift        # Clipboard read/write
│   │   └── ArtifactThumbnailGenerator.swift # Thumbnail generation
│   │
│   ├── extensions/                        # Swift extensions
│   │   ├── Color+AccentColor.swift        # Color extensions
│   │   ├── Color+NotchColor.swift         # Notch color customization
│   │   ├── Button+Bouncing.swift          # Bouncing button animation
│   │   ├── ConditionalModifier.swift      # View conditional modifier
│   │   ├── PanGesture.swift               # Pan gesture recognizer
│   │   ├── KeyboardShortcutsHelper.swift  # Shortcut definitions
│   │   ├── NSImage+Extensions.swift       # NSImage helpers
│   │   ├── NSScreen+UUID.swift            # Screen UUID extension
│   │   └── DataTypes+Extensions.swift     # Data type extensions
│   │
│   ├── Shortcuts/
│   │   ├── RecordedShortcut.swift          # Custom shortcut recording model
│   │   └── ShortcutConstants.swift         # Shortcut key constants
│   │
│   ├── theme/
│   │   ├── ThemeManager.swift             # Light/Dark/System theme + VEColors + VEFonts
│   │   └── ThemeShowcase.swift            # Theme preview (debug)
│   │
│   ├── menu/
│   │   ├── StatusBarMenu.swift            # Menu bar dropdown items
│   │   └── MenuBarMeetingsSection.swift   # Upcoming meetings in menu
│   │
│   ├── animations/
│   │   └── drop.swift                     # Drop animation
│   │
│   ├── metal/
│   │   └── visualizer.metal               # Metal shader for audio visualizer
│   │
│   ├── sizing/
│   │   └── matters.swift                  # Notch sizing calculations
│   │
│   ├── observers/
│   │   └── FullscreenMediaDetection.swift # Fullscreen app detection
│   │
│   ├── private/
│   │   └── CGSSpace.swift                 # Private CGS API for Spaces
│   │
│   ├── enums/
│   │   └── generic.swift                  # Generic enums (NotchState, NotchViews, etc.)
│   │
│   ├── utils/
│   │   ├── AppConstants.swift             # URLs, onboarding video URLs
│   │   └── Logger.swift                   # VEAILog file logger
│   │
│   ├── XPCHelperClient/
│   │   ├── VEAIXPCHelperProtocol.swift    # XPC protocol definition
│   │   └── XPCHelperClient.swift          # XPC client for accessibility
│   │
│   ├── Assets.xcassets/                   # App icons, images, colors
│   ├── Info.plist                         # App configuration
│   ├── VEAI.entitlements                  # Release entitlements
│   └── VEAI-Debug.entitlements            # Debug entitlements
│
├── VEAIXPCHelper/                         # XPC Helper target
│   ├── Info.plist
│   └── VEAIXPCHelper.entitlements
│
├── VEAIAppActivityHelper/                 # Activity monitoring helper
│   └── VEAIAppActivityHelper.entitlements
│
├── Configuration/
│   ├── dmg/                               # DMG build scripts
│   │   ├── create-dmg.sh                  # DMG creation script
│   │   ├── fix-app-signing.sh             # Code signing fix
│   │   └── dmgbuild_settings.py           # DMG layout settings
│   └── sparkle/                           # Sparkle update tools
│       ├── generate_appcast               # Appcast generator
│       └── generate_keys                  # Ed25519 key generator
│
├── updater/
│   └── appcast.xml                        # Sparkle update feed
│
├── VE.xcodeproj/                          # Xcode project
├── .env.example                           # Environment variable template
├── CLAUDE.md                              # AI context documentation
├── README.md                              # Project readme
├── CONTRIBUTING.md                        # Contributing guidelines
├── LICENSE                                # License file
└── THIRD_PARTY_LICENSES                   # Third-party licenses
```

---

## STEP 3 — UI Architecture

### 3.1 Notch Overlay (Floating Transparent Window)

**File:** `VE/ContentView.swift`
**Framework:** SwiftUI inside an AppKit NSPanel
**Hierarchy:**
```
ContentView
├── NotchLayout
│   ├── [closed] InlineHUD (sneak peek)
│   ├── [closed] PermissionHUD
│   ├── [closed] ClosedNotchContentView
│   │   ├── Idle state (VE icon + status icons)
│   │   ├── Prediction states (waiting/streaming/success/error)
│   │   ├── Dictation states (waiting/recording/processing/success/error)
│   │   ├── Meeting states (starting/active/paused/result/error)
│   │   ├── Update banner
│   │   ├── Welcome message
│   │   └── Error display
│   └── [open] NotchHomeView
│       ├── AuthWrapper → UnauthorizedView (login)
│       └── Authorized content
│           ├── Navigation tabs (Chat, Home, Meeting)
│           ├── ChatView
│           ├── HomeView
│           └── MeetingView
└── Chin (transparent hit area below notch)
```

**User Interactions:**
- **Hover** (unauthenticated): Opens login notch after 300ms delay
- **Tap** (unauthenticated): Opens login notch
- **Pan gesture down** (closed): Opens notch with spring animation
- **Pan gesture up** (open): Closes notch
- **Key holds**: Trigger prediction/dictation/instruction (see Step 5)

**Animations:** `Animation.interactiveSpring(response: 0.38, dampingFraction: 0.8)` for open/close. `Animation.spring(response: 0.42, dampingFraction: 0.8)` for content transitions.

### 3.2 Floating Window (Chat/Instruction Panel)

**File:** `VE/FloatingWindowController.swift`
**Framework:** AppKit NSPanel (KeyablePanel) with SwiftUI content
**Behavior:** Always-on-top, transparent, draggable, positioned below notch. Used for voice instruction transcription display and quick chat.

### 3.3 Settings Window

**File:** `VE/components/Settings/SettingsWindowController.swift`
**Framework:** AppKit NSWindow with SwiftUI content
**Hierarchy:**
```
SettingsLayout
├── SettingSidebar (navigation)
│   ├── Apps section
│   │   ├── Home
│   │   ├── Chat
│   │   ├── Meeting Notes
│   │   ├── Voice
│   │   ├── Knowledge
│   │   ├── Memory
│   │   ├── Dictionary
│   │   └── Connectors
│   └── Settings section
│       ├── My Profile
│       ├── Shortcuts
│       ├── Notch
│       ├── Workspace
│       ├── Team Members
│       ├── Subscription
│       ├── Share & Earn
│       └── About
└── Content area (switches based on sidebar selection)
```

### 3.4 Onboarding Flow

**Files:** `VE/components/Onboarding/OnboardingFlowView.swift` and related
**Framework:** SwiftUI in AppKit NSWindow
**Steps:**
1. Welcome → 2. Permissions Request → 3. Try Prediction → 4. Try Dictation → 5. Connectors → 6. Completion

### 3.5 Menu Bar

**File:** `VE/VEAIApp.swift` (MenuBarExtra scene)
**Items:** Open Chat, Open Work, Quick Note, Settings, Shortcuts, Microphone selector, Pause Context Collection, Exclude Current App, Version, Check for Updates, Help Center, Talk to Support, Restart, Quit.

---

## STEP 4 — API Integrations

### 4.1 Authentication

| Endpoint | Method | Description | File |
|----------|--------|-------------|------|
| `https://auth.ve.ai/refresh-token` | POST | Refresh JWT access token | `TokenRefreshService.swift` |
| `https://ve.ai/auth/desktop-login` | GET | Desktop login handoff URL | `AuthStorage.swift` |
| `ve://oauth/callback` | URL Scheme | OAuth callback from browser | `AppDelegate+URLHandling.swift` |

**Headers:** `x-access-token` (JWT), `x-csrf-token` (CSRF for POST/PUT/DELETE), `Content-Type: application/json`
**Auth:** JWT token in `x-access-token` header. httpOnly refresh token cookie sent automatically.

### 4.2 REST API Endpoints

All REST calls go through `NetworkService.swift` which adds auth headers, handles CSRF, and retries on 401 (jwt expired).

**Base URLs (region-dependent):**
- US: `https://us.api.ve.ai/{service}/1.0`
- AP: `https://ap.api.ve.ai/{service}/1.0`

| Service | Endpoint Pattern | File |
|---------|-----------------|------|
| Tenant Users | `tenant-users/1.0/*` | `NetworkService.swift` |
| Meeting | `meeting/1.0/*` | `MeetingService.swift` |
| Calendar | `google/1.0/*` | `CalendarService.swift` |
| AI Agents | `agents/1.0/*` | `KnowledgeAgentService.swift` |
| Third-party | `third-party-integrations/1.0/*` | `ConnectorsService.swift` |
| Microsoft | `microsoft-integration/1.0/*` | `ConnectorsService.swift` |
| User Activity | `tenant-users/1.0/user-activity/*` | `VEAIApp.swift` (BlockedAppsService) |
| Folders | `folders/1.0/*` | Various |
| Galleries | `galleries/1.0/*` | Various |

### 4.3 WebSocket Endpoints

| WebSocket | URL | Purpose | Client File |
|-----------|-----|---------|------------|
| Unified Audio (Prediction) | `wss://cursor-intelligence.us-east-1.ve.ai` | Audio + screenshot → AI prediction text | `UnifiedAudioSocketClient.swift` |
| Dictation | `wss://voice-intelligence.us-east-1.ve.ai` | Audio → enhanced transcription | `UnifiedAudioSocketClient.swift` (shared) |
| Chat/Instructions | `wss://ai.us-east-1.ve.ai` | Chat messages → AI responses | `MultiAgentSocketClient.swift` |
| Guest Chat | `wss://guestsearch.us-east-1.ve.ai` | Guest user chat | `MultiAgentSocketClient.swift` |
| Meeting | `wss://recall.us-east-1.ve.ai` | Meeting audio → transcription | `WebSocketRegistry.swift` |
| Voice Enrollment | `wss://voice-intelligence.us-east-1.ve.ai` | Voice profile enrollment | `VoiceToTextSocketClient.swift` |

**WebSocket Protocol:**
- Binary frames: PCM audio (16kHz, 16-bit, mono)
- Text frames: JSON payloads (action, metadata, end payload)
- Auth: Token passed as query parameter in connection URL

### 4.4 External Service Integrations

| Service | Purpose | Config Key |
|---------|---------|-----------|
| Sentry | Crash reporting | `SENTRY_DSN` in Info.plist |
| Slack | Error logging webhooks | `SLACK_ERROR_WEBHOOK_URL` in Info.plist |
| Intercom | In-app help center | `INTERCOM_APP_ID` in Info.plist |
| Sparkle | Auto-updates | `SUFeedURL` → `https://veaiinc.github.io/ve-macos-app-releases/appcast.xml` |

---

## STEP 5 — Event Triggers

### 5.1 Keyboard Triggers (CGEventTap)

The app uses `LowLevelKeyTap` (CGEventTap) to monitor modifier keys globally. Events are consumed (blocked from reaching other apps) when bound to an action.

| Trigger | Default Key | Action | Hold Threshold | File |
|---------|------------|--------|---------------|------|
| AI Prediction | Control (hold) | Captures screenshot + audio → sends to cursor-intelligence WS | 350ms | `KeyboardMonitor.swift` |
| Voice Dictation | Disabled (configurable) | Captures audio → sends to voice-intelligence WS → pastes result | 350ms | `KeyboardMonitor.swift` |
| Voice Instruction | Option (hold) | Captures audio → sends to multiagent WS → shows floating result | 350ms | `KeyboardMonitor.swift` |
| Meeting Toggle | Fn (double-tap) | Start/stop meeting transcription | 500ms between taps | `KeyboardMonitor.swift` |
| Update Trigger | Fn (tap) | Triggers pending update when banner visible | Immediate | `LowLevelKeyTap.swift` |
| Cancel | Escape | Cancels active prediction/dictation/instruction | Immediate | `EscEventTapManager.swift` |
| Prediction Feedback | Enter/Return | Captures text via clipboard for feedback (15s window) | Immediate | `PredictionFeedbackService.swift` |
| Paste Detection | Cmd+V | Detected (not consumed) for clipboard cleanup | Immediate | `LowLevelKeyTap.swift` |

**Configurable shortcut system:** Users can set modifier-only shortcuts (hold key) or key-combo shortcuts (e.g., Cmd+K) for prediction, dictation, and instruction actions via `RecordedShortcut`.

### 5.2 App Lifecycle Events

| Event | Handler | Action |
|-------|---------|--------|
| App launch | `AppDelegate+Launch.swift` | Init all services, check permissions, create windows |
| App activate | `AuthManager+TokenRefresh.swift` | Check and refresh token |
| App resign active | `AppDelegate+Launch.swift` | Switch to accessory mode if Settings closed |
| System wake | `AppDelegate+Launch.swift` | Restart app (full relaunch) |
| System sleep | `AuthManager+TokenRefresh.swift` | Check token before sleep |
| Screen lock | `AppDelegate.swift` | Hide notch |
| Screen unlock | `AppDelegate.swift` | Show notch |
| Screen config change | `AppDelegate+Launch.swift` | Reposition windows |
| URL scheme (`ve://`) | `AppDelegate+URLHandling.swift` | Handle OAuth/integration callbacks |

### 5.3 Timer Events

| Timer | Interval | Purpose | File |
|-------|----------|---------|------|
| Token refresh scheduler | Dynamic (1min before expiry) | Proactive JWT refresh | `AuthManager+TokenRefresh.swift` |
| Periodic token check | 30 seconds | Safety net for missed refreshes | `AuthManager+TokenRefresh.swift` |
| Update check | 10 minutes | Check for Sparkle updates | `AppDelegate+UpdateChecker.swift` |
| Prediction feedback | 15 seconds | Window for capturing feedback on Enter | `PredictionFeedbackService.swift` |
| Activity pause resume | Configurable (5/15/30/60 min) | Resume context collection after pause | `VEAIApp.swift` |
| WebSocket idle timeout | Configurable | Disconnect idle WebSocket | `WebSocketTransport.swift` |
| Notch hide delay | 2 seconds | Smooth hide transition | `VEAIViewModel.swift` |
| Release poller | 500ms | Poll CGEventSource.keyState for key release | `LowLevelKeyTap.swift` |

### 5.4 WebSocket Events

| Event | Source | Handler | Action |
|-------|--------|---------|--------|
| `suggested_text` | cursor-intelligence | `UnifiedAudioSocketClient` | Display prediction, auto-paste |
| `enhanced_text` | voice-intelligence | `UnifiedAudioSocketClient` | Display dictation result, auto-paste |
| `transcription` | multiagent | `MultiAgentSocketClient` | Show real-time transcription in floating bar |
| `stream_end` | multiagent | `MultiAgentSocketClient` | Close floating window, show result |
| `answer` | multiagent (chat mode) | `MultiAgentSocketClient` | Stream chat response chunks |
| `error` | any | respective client | Show error in notch |
| Connection lost | URLSession | `WebSocketTransport` | Auto-reconnect with exponential backoff |

### 5.5 Notification Center Events (Internal)

| Notification | Purpose |
|-------------|---------|
| `NavigateToNotes` | Navigate to Notes tab in settings |
| `NavigateToMeetingSummary` | Open specific meeting summary |
| `NavigateToConnectors` | Navigate to Connectors settings |
| `NavigateToShortcuts` | Navigate to Shortcuts settings |
| `OpenIntercomHelp` | Open Intercom help widget |
| `TokenRefreshed` | Reschedule token refresh timer |
| `IntegrationOAuthCallback` | Integration OAuth completed |
| `ThemeChanged` | Theme preference changed |
| `authStateChanged` | Auth state transitioned |
| `meetingListNeedsRefresh` | Refresh meeting list data |
| `CloseNotchOnNoInternet` | Force close notch on network loss |

---

## STEP 6 — Business Logic

### 6.1 AI Prediction Pipeline

```
User holds Control key (350ms threshold)
  → KeyboardMonitor detects hold via LowLevelKeyTap (CGEventTap)
  → UnifiedAudioService.startPrediction()
    → Pre-start audio capture (0ms delay for mic warmup)
    → Capture screenshot of active window (via XPC helper)
    → Connect to cursor-intelligence WebSocket
    → Stream PCM audio in 100ms chunks (16kHz, 16-bit, mono)
    → Send screenshot as base64 in initial payload
  → User releases Control key
    → Send end payload with metadata (timezone, platform, location)
    → UnifiedAudioSocketClient receives `suggested_text` response
    → Auto-paste text into active app (via PasteHelper → Cmd+V simulation)
    → Start 15s feedback window (PredictionFeedbackService)
    → If user presses Enter within 15s: capture actual text via clipboard, send feedback
```

### 6.2 Voice Dictation Pipeline

```
User holds configured key (350ms threshold)
  → DictationService.preStartAudioCapture() (immediate mic on)
  → After 350ms: DictationService.startDictation()
    → Check mic permission
    → Resolve microphone (Bluetooth override if needed)
    → Start AVAudioEngine
    → Connect to voice-intelligence WebSocket
    → Flush buffered audio from pre-start
    → Stream 100ms PCM chunks
  → User releases key
    → DictationService.signalStopRecording() (immediate mic off + UI update)
    → DictationService.stopDictation()
      → Flush remaining audio
      → Scrape active window metadata (via XPC)
      → Send end payload
    → UnifiedAudioSocketClient receives `enhanced_text`
    → Auto-paste transcribed text into active app
    → Save recording locally for voice logs
```

### 6.3 Voice Instruction Pipeline

```
User holds Option key (350ms threshold)
  → KeyboardMonitor triggers instruction action
  → UnifiedAudioService starts audio capture
  → Connect to multiagent WebSocket
  → Stream audio chunks
  → User releases Option key
  → Send end payload
  → MultiAgentSocketClient receives streaming `transcription` events
    → Show real-time transcription in FloatingWindowController
  → On `stream_end`: display final result
  → User can interact with result (copy, retry, close)
```

### 6.4 Meeting Transcription

```
Meeting detection (automatic or manual):
  → MeetingDetectionService monitors calendar events
  → Shows "Meeting detected" popup
  → User confirms or starts manually (Fn double-tap)
  → MeetingService.startMeeting()
    → Connect to recall WebSocket
    → Start audio capture (mic + optional screen audio via ScreenCaptureKit)
    → Stream audio chunks to backend
    → Receive real-time transcription
  → Meeting ends (manual stop or auto-detection)
    → Send end payload
    → Navigate to meeting summary view
```

### 6.5 Chat System

```
User opens chat (via menu bar, keyboard shortcut Cmd+., or notch)
  → ChatManager manages message history
  → User types message in ChatInputField
  → MultiAgentSocketClient sends message via WebSocket
    → Mode set to .chat
    → Streaming answer chunks received
    → Citations and code blocks parsed (ChatContentParser)
  → Response displayed with:
    → Markdown rendering (MarkdownUI)
    → Code syntax highlighting (CodePreviewWebView)
    → Citation pills with source links
    → Copy/retry action buttons
```

### 6.6 Activity Monitoring Pipeline

```
App launch + Accessibility granted
  → AppActivityService.startObserver()
    → Monitors app switches via NSWorkspace notifications
    → Captures active window title, app name, URL (for browsers)
  → LocalStoreService stores activity data in SQLite
  → ActivityPipelineService pipeline:
    → Capture → Accumulate → Seal → Sync (to backend)
  → Respects user preferences:
    → Excluded apps (BlockedAppsService)
    → Pause for duration (ActivityPauseTimer)
    → Activity monitoring toggle
```

---

## STEP 7 — Authentication System

### 7.1 Login Flow

1. User clicks "Login with Microsoft" or "Login with Google" in the notch
2. `AuthManager.loginWithMicrosoft()` / `loginWithGoogle()`:
   - Stores `authProvider` (.outlook / .google)
   - Opens browser to `https://auth.ve.ai/login/{provider}`
   - Sets `authState = .authenticating`
3. OAuth completes → browser redirects to `ve://oauth/callback?code=...`
4. `AppDelegate+URLHandling` receives the URL → calls `AuthManager.handleOAuthCallback()`
5. AuthManager exchanges code for tokens:
   - Calls exchange API → receives `accessToken`, `accessTokenExpiry`, `csrfToken`, `workspaceId`, `region`, etc.
   - Saves to `AuthStorage` (Keychain + UserDefaults + HTTPCookies)
   - Sets `authState = .authorized`
6. Post-auth API calls:
   - Fetch user profile
   - Fetch tenant/workspace settings
   - Pre-warm WebSocket connections
   - Start token refresh scheduler
   - Update Sentry user context

### 7.2 Token Storage

| Credential | Debug Build | Release Build |
|-----------|-------------|---------------|
| `usertoken` (JWT) | UserDefaults | macOS Keychain (Data Protection) |
| `csrfToken` | UserDefaults | macOS Keychain |
| `accessTokenExpiry` | UserDefaults | macOS Keychain |
| `refreshTokenExpiry` | UserDefaults | macOS Keychain |
| `refreshToken` | httpOnly cookie (managed by backend) | httpOnly cookie |

**Keychain Service:** `VE/helpers/KeychainService.swift`
- Uses `kSecUseDataProtectionKeychain` (modern, silent, no password prompts)
- Falls back to UserDefaults with `__kc_` prefix if Data Protection keychain unavailable
- One-time migration from UserDefaults → Keychain on first Release build launch

### 7.3 Token Refresh

- **Proactive refresh:** Scheduled 60s before token expiry via `Timer`
- **Periodic check:** Every 30 seconds, checks if token expires within 180s
- **Lifecycle awareness:** Refreshes on app activate, system wake, before sleep
- **Retry:** Up to 5 attempts with delays [2, 5, 10, 15, 20] seconds
- **Permanent errors:** "jwt expired", "invalid refresh token" → auto-logout
- **Cooldown:** 10s minimum between refresh attempts
- **Concurrency:** Singleton lock ensures only one refresh at a time

### 7.4 Cookie Management

Cookies stored in `HTTPCookieStorage.shared` with domain `.ve.ai`:
- `usertoken`, `csrfToken`, `accessTokenExpiry`, `region`, `workspaceId`, `workspaceMode`, `tenant_id`, `isOnboard`, `locationDetails`

---

## STEP 8 — Background Processes

### 8.1 XPC Helper Service (`VEAIXPCHelper`)

- **Purpose:** Privileged operations requiring accessibility permissions
- **Capabilities:**
  - Screen reading (active window text extraction)
  - Caret position detection
  - Accessibility authorization monitoring
  - Window context scraping (title, URL, visible text)
- **Communication:** AsyncXPCConnection library over Mach port
- **Protocol:** `VEAIXPCHelperProtocol`

### 8.2 WebSocket Connections (Always-On)

Managed by `WebSocketRegistry` (singleton):
- **UnifiedAudio transport:** Pre-established for instant prediction
- **Dictation transport:** Shared transport with unified audio
- **MultiAgent transport:** For chat and voice instructions
- **Meeting transport:** Active only during meetings
- All use `WebSocketTransport` with auto-reconnect and exponential backoff

### 8.3 Audio Processing

**AudioHelper** (`VE/helpers/AudioHelper.swift`):
- AVAudioEngine-based audio capture
- 16kHz, 16-bit, mono PCM format
- Pre-start buffering (captures audio before WebSocket connects)
- 100ms chunk batching timer
- Bluetooth override: prefer built-in mic to preserve A2DP audio quality

**MeetingAudioStreamer** (`VE/helpers/MeetingAudioStreamer.swift`):
- Dual capture: microphone + screen audio (ScreenCaptureKit)
- Echo gating to prevent double-transcription

### 8.4 Activity Pipeline

**AppActivityService → LocalStoreService → ActivityPipelineService:**
- Capture: App switches, tab changes, window titles
- Store: SQLite database in app sandbox
- Sync: Periodic upload to backend

### 8.5 OS Integrations

| Integration | API | Purpose |
|------------|-----|---------|
| Accessibility | `AXIsProcessTrusted()`, CGEventTap | Keyboard monitoring, screen reading |
| Microphone | `AVCaptureDevice`, AVAudioEngine | Audio capture for prediction/dictation |
| Screen Recording | ScreenCaptureKit | Screenshot for prediction, meeting audio |
| Notifications | APNs (UserNotifications) | Push notifications |
| Calendar | Google/Microsoft APIs | Meeting detection and scheduling |
| Sleep Prevention | `IOPMAssertionCreateWithName` | Keep system awake during meetings |
| Network Monitoring | NWPathMonitor | Detect connectivity changes |
| Screen Lock | DistributedNotificationCenter | Hide notch when screen locked |
| Launch at Login | LaunchAtLogin framework | Auto-start on boot |

---

## STEP 9 — External Dependencies

### Swift Package Manager Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| **Sparkle** | ≥ 2.8.0 | Auto-update framework (DMG distribution) |
| **LaunchAtLogin-Modern** | ≥ 1.1.0 | Launch at login capability |
| **KeyboardShortcuts** | ≥ 2.2.4 | Global keyboard shortcut registration |
| **Defaults** | ≥ 9.0.2 | Type-safe UserDefaults wrapper |
| **sentry-cocoa** | ≥ 8.45.0 | Crash reporting and error tracking |
| **SkyLightWindow** | ≥ 1.0.0 | Window effect utilities |
| **lottie-spm** | ≥ 4.5.2 | Lottie animation playback |
| **AsyncXPCConnection** | ≥ 1.3.0 | Async XPC communication |
| **MacroVisionKit** | ≥ 0.2.0 | Vision/ML utilities |
| **swift-bson** | ≥ 3.1.0 | BSON serialization for WebSocket |
| **HotKey** | ≥ 0.1.0 | Global hotkey registration |
| **swift-markdown-ui** | ≥ 2.4.0 | Markdown rendering in SwiftUI |

### System Frameworks

| Framework | Usage |
|-----------|-------|
| AppKit | Window management, NSPanel, menu bar |
| SwiftUI | UI views and state management |
| AVFoundation | Audio capture (microphone) |
| Security | Keychain access (credential storage) |
| Metal | GPU-accelerated audio visualizer shader |
| QuartzCore | Animations, CALayer |
| ApplicationServices | CGEventTap, accessibility APIs |
| ScreenCaptureKit | Screen capture for prediction + meeting audio |
| Network | NWPathMonitor for connectivity |
| UserNotifications | Push notifications |
| UniformTypeIdentifiers | File type identification |
| WebKit | Code preview rendering |

---

## STEP 10 — Build System

### Build Configuration

- **Xcode Project:** `VE.xcodeproj`
- **Build System:** Xcode with Swift Package Manager for dependencies
- **Targets:**
  1. `VE` — Main app (menu bar app)
  2. `VEAIXPCHelper` — XPC helper service
  3. `VEAIAppActivityHelper` — Activity monitoring helper

### Build & Run

```bash
# Open in Xcode
open VE.xcodeproj

# Build from command line
xcodebuild -project VE.xcodeproj -scheme VE -configuration Release build

# Create DMG for distribution
cd Configuration/dmg
./create-dmg.sh
```

### Environment Variables (via Info.plist)

| Key | Value | Purpose |
|-----|-------|---------|
| `SENTRY_DSN` | `https://3317a0b1...@sentry.io/...` | Sentry crash reporting |
| `SENTRY_ENVIRONMENT` | `production` | Sentry environment tag |
| `SLACK_ERROR_WEBHOOK_URL` | `https://hooks.slack.com/services/...` | Error logging webhook |
| `INTERCOM_APP_ID` | `jkw0oti4` | Intercom help center |
| `SUFeedURL` | `https://veaiinc.github.io/.../appcast.xml` | Sparkle update feed |
| `SUPublicEDKey` | Ed25519 public key | Sparkle signature verification |

### Platform Requirements

- macOS 14.0 Sonoma or later
- Apple Silicon (arm64) or Intel (x86_64)
- Accessibility permission required
- Microphone permission required
- Screen recording permission required (for prediction screenshots)

### Distribution

- **NOT App Store** — distributed via direct DMG download
- Auto-updates via Sparkle framework (Ed25519 signed)
- Appcast XML hosted at: `https://veaiinc.github.io/ve-macos-app-releases/appcast.xml`
- Update preferences: auto-check enabled, auto-download enabled, skip not allowed

---

## STEP 11 — Security Review

### 11.1 Credentials & Secrets in Codebase

| Item | Location | Risk |
|------|----------|------|
| Sentry DSN | `Info.plist` | Low — DSN is semi-public, only allows sending events |
| Slack Webhook URL | `Info.plist` | **Medium** — allows posting to Slack channel |
| Sparkle Ed25519 Public Key | `Info.plist` | Low — public key, needed for update verification |
| Intercom App ID | `Info.plist` | Low — semi-public identifier |

### 11.2 Credential Storage Security

- **Release builds:** JWT tokens stored in macOS Keychain (`kSecUseDataProtectionKeychain`)
- **Debug builds:** Tokens in UserDefaults (plaintext) — acceptable for development
- **Refresh token:** httpOnly cookie — never accessible to client code
- **Keychain migration:** Auto-migrates from UserDefaults → Keychain on first Release launch

### 11.3 Network Security

- **Certificate Pinning:** `CertificatePinning.swift` — validates server certificates in URLSession delegate
- **CSRF Protection:** `x-csrf-token` header on all POST/PUT/DELETE requests
- **Token validation:** Automatic retry on 401 with token refresh
- **Cookie scoping:** Domain `.ve.ai` with `Secure` flag

### 11.4 Permissions Required

| Permission | macOS API | Usage |
|-----------|-----------|-------|
| **Accessibility** | `AXIsProcessTrusted()` | CGEventTap keyboard monitoring, screen reading |
| **Microphone** | `AVCaptureDevice.requestAccess(for: .audio)` | Voice dictation, prediction, meeting transcription |
| **Screen Recording** | ScreenCaptureKit | Screenshots for AI prediction context |
| **Push Notifications** | UserNotifications | Remote notifications |

### 11.5 Privacy Considerations

- **Sentry:** User email is stripped from crash reports (`event.user?.email = nil`)
- **Slack:** User info (email, workspace, region) included in error reports for debugging
- **Activity Monitoring:** Users can pause, exclude apps, or disable entirely
- **Token URLs:** Auth endpoints redacted in Sentry breadcrumbs

### 11.6 Potential Security Risks

1. **Slack webhook URL in Info.plist** — could be extracted from app bundle. Consider moving to server-side logging.
2. **URL sanitization** — `URLSanitizer.swift` exists but coverage should be verified for all user-input URLs.
3. **XPC communication** — XPC helper runs with elevated privileges; protocol should validate calling app.
4. **Clipboard access** — PredictionFeedbackService reads/writes clipboard (Cmd+A, Cmd+C simulation) — could capture sensitive data in the 15s feedback window.

---

## STEP 12 — Runtime Flow

### Complete App Lifecycle

```
1. APP LAUNCH
   └── DynamicNotchApp.init()
       ├── Initialize FileLogger
       ├── Initialize PredictionFeedbackService
       ├── Configure Sparkle updater (auto-check + auto-download)
       ├── Enable LaunchAtLogin (first launch only)
       ├── Initialize Sentry (ErrorService.configureSentry())
       └── Setup crash handler (NSSetUncaughtExceptionHandler)

2. SCENE BODY
   └── MenuBarExtra (menu bar icon)
       ├── MicrophoneMenuContent
       ├── PauseContextCollectionMenu
       ├── ExcludeCurrentAppMenu
       └── Menu items (Chat, Work, Settings, Shortcuts, etc.)

3. applicationDidFinishLaunching
   ├── Initialize WebSocketRegistry (creates transport objects)
   ├── Initialize FloatingWindowController
   ├── Start NetworkMonitor (connectivity)
   ├── Preload remote assets (ImagePreloader)
   ├── Prefetch voice logs data
   ├── Setup notification observers (50+ observers)
   ├── Register ve:// URL scheme handler
   ├── Check permissions (Accessibility → Microphone → Screen Recording)
   ├── Setup keyboard trigger (LowLevelKeyTap + KeyboardMonitor)
   ├── Create VEAIViewModel and notch window
   ├── Check for updates immediately
   ├── Start AppActivityService (if accessibility granted)
   └── Trigger onboarding if first launch

4. AUTHENTICATION CHECK
   ├── AuthStorage checks for existing token (Keychain/UserDefaults)
   ├── If token exists and valid:
   │   ├── Set authState = .authorized
   │   ├── Setup token refresh scheduler (periodic + scheduled)
   │   ├── Fetch user profile
   │   ├── Fetch tenant settings
   │   ├── Pre-warm WebSocket connections
   │   ├── Start meeting detection
   │   └── Show welcome message in notch (3s display)
   └── If no token:
       ├── Set authState = .unauthorized
       └── Show login notch (hover/tap to expand)

5. IDLE STATE
   ├── Notch overlay visible (small bar near macOS notch)
   │   ├── Shows VE icon + status indicators
   │   ├── Meeting icon (if active meeting)
   │   └── Instruction history icon (if recent instruction)
   ├── WebSocket connections maintained (with idle timeout)
   ├── Token refresh timer running (30s periodic check)
   ├── Activity monitoring active (if enabled)
   └── Network monitor watching connectivity

6. USER INTERACTION: PREDICTION
   ├── User holds Control key
   ├── 350ms delay → KeyboardMonitor.onHold(.prediction)
   ├── UnifiedAudioService.startPrediction()
   │   ├── Pre-start mic (immediate audio buffer)
   │   ├── Capture screenshot (XPC helper)
   │   ├── Connect/verify WebSocket
   │   ├── Stream audio + send screenshot
   │   └── UI: Notch shows "Thinking..." with dot loader
   ├── User releases Control key
   │   ├── Stop mic, flush audio, send end payload
   │   └── UI: Notch shows "Processing..."
   ├── WebSocket receives suggested_text
   │   ├── Auto-paste into active app
   │   ├── UI: Notch shows success (green checkmark) for 4s
   │   └── Start 15s feedback window
   └── Notch returns to idle state

7. USER INTERACTION: DICTATION
   ├── Similar to prediction but:
   │   ├── No screenshot capture
   │   ├── Uses voice-intelligence WebSocket
   │   ├── Receives enhanced_text (AI-improved transcription)
   │   └── Saves recording locally for voice logs

8. USER INTERACTION: CHAT
   ├── User opens via Cmd+. or menu bar
   ├── SettingsWindowController.showWindow()
   ├── ChatView with message history
   ├── User sends message → MultiAgentSocketClient
   ├── Streaming response displayed
   └── Citations and code blocks rendered

9. BACKGROUND OPERATIONS (CONTINUOUS)
   ├── Token refresh (every 30s check + scheduled refresh)
   ├── WebSocket health checks
   ├── Activity monitoring pipeline (capture → store → sync)
   ├── Meeting detection (calendar polling)
   ├── Network connectivity monitoring
   └── Update checking (every 10 minutes)

10. APP TERMINATION
    ├── Stop AppActivityService + ActivityPipelineService
    ├── Remove all notification observers
    ├── Invalidate all timers
    ├── Stop XPC helper monitoring
    ├── Teardown audio engine
    ├── Restore system default audio input
    └── Clean up windows
```

---

## Appendix A — Key Singletons

| Singleton | Class | Purpose |
|-----------|-------|---------|
| `AuthManager.shared` | `AuthManager` | Authentication state and OAuth flow |
| `AuthStorage.shared` | `AuthStorage` | Token and profile persistence |
| `NetworkService.shared` | `NetworkService` | HTTP client with auth |
| `BaseURLService.shared` | `BaseURLService` | Multi-region URL resolution |
| `WebSocketRegistry.shared` | `WebSocketRegistry` | WebSocket lifecycle management |
| `VEAIViewCoordinator.shared` | `VEAIViewCoordinator` | Central UI state coordinator |
| `KeyboardMonitor.shared` | `KeyboardMonitor` | Keyboard event handling |
| `ThemeManager.shared` | `ThemeManager` | Light/dark theme management |
| `ErrorService.shared` | `ErrorService` | Error logging (Sentry + Slack) |
| `TokenRefreshService.shared` | `TokenRefreshService` | JWT token refresh |
| `DictationService.shared` | `DictationService` | Voice dictation workflow |
| `UnifiedAudioService.shared` | `UnifiedAudioService` | Prediction audio/screenshot |
| `MeetingService.shared` | `MeetingService` | Meeting lifecycle |
| `PredictionFeedbackService.shared` | `PredictionFeedbackService` | Post-prediction feedback |
| `FloatingWindowController.shared` | `FloatingWindowController` | Floating chat panel |
| `SettingsWindowController.shared` | `SettingsWindowController` | Settings window |
| `AudioHelper.shared` | `AudioHelper` | AVAudioEngine wrapper |
| `XPCHelperClient.shared` | `XPCHelperClient` | XPC communication |

## Appendix B — Theme Colors

| Color Name | Dark Mode | Light Mode |
|-----------|-----------|------------|
| Background | `#151719` | `#FAFAFA` |
| Card | `#25292D` | `#F3F3F3` |
| Blue (accent) | `#007CEC` | `#007CEC` |
| Text Primary | `#F4F5F5` | `#394046` |
| Text Secondary | `#7C8388` | `#7C8388` |
| Red (error) | `#FF4B59` | `#FF4B59` |
| Green (success) | `#00CA48` | `#00CA48` |
| Yellow (warning) | `#FFC600` | `#FFC600` |

**Custom Font:** GeneralSans (Regular, Medium) — loaded via CTFontManager

---

*Document generated: 2026-03-14*
*Source: ve-macos-desktop-app repository (master branch)*
*Total Swift files: ~180 | Estimated LOC: ~40,000+*
