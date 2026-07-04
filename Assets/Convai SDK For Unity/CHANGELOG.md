# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Released]

## [4.1.0] - 2026-04-09
### Feature Additions
- Added dynamic context support.
- Made the LipSync sample's showcase camera work without a hard dependency on the Unity Input System by switching to reflection-based optional support.
- Refined the packaged sample scenes and transcript chat prefab defaults for a smoother out-of-box setup experience.

### Bug Fixes and Improvements
- Improved Vision module behavior and reliability.
- Improved editor startup reliability to reduce first-load native plugin errors during package import.
- Fixed compile-time issues around optional support libraries and improved native plugin compatibility for different Windows Unity editor architectures.
- Fixed an iOS crash related to support library handling.
- Fixed LipSync showcase eye-contact blend shape discovery so characters whose face meshes live outside the eye-bone subtree resolve correctly.
- Tuned showcase camera behavior and related sample assets for more stable presentation in the LipSync sample.

## [4.0.0] - 2026-03-12
### Feature Additions
- Introduced initial LipSync support in the Unity Core SDK, including core runtime integration points for LipSync-driven character workflows.
- Added bundled default ARKit blendshape maps to streamline early LipSync setup and reduce manual configuration.
- Expanded initial WebGL + LiveKit support, including support for vision canvas publishing in WebGL environments.
- Added an initial configurable native runtime mode to support evolving runtime selection behavior across supported platforms.
- Introduced early session resume UI and remote audio control support as part of the ongoing session and media control workflow improvements.
- Added support for passing emotion_config in room connection payloads.
- Continued evolving the SDK API surface with ConvaiRoomSession and broader session-oriented facade changes.
- Introduced a platform-aware networking bootstrap flow to support cleaner runtime registration for native and WebGL networking implementations.

### Bug Fixes and Improvements
- Improved stability of LipSync room-connect transport integration and related runtime connection flows.
- Continued simplifying the networking stack by removing older orchestration, reconnection, and legacy connection service layers in favor of more direct runtime ownership.
- Improved reliability of manager-driven startup and bootstrap behavior under the ConvaiManager flow.
- Updated native library download handling so native libraries are imported into a writable Unity project location, improving package install compatibility.
- Improved UPM sample packaging and sample structure to better align with Unity package distribution expectations.
- Resolved a number of compiler warnings, runtime integration issues, and editor UI sizing and stability issues in configuration and setup surfaces.

## [0.1.0] - 2026-02-20

### Added
- Real-time conversational AI characters via `ConvaiCharacter`, `ConvaiPlayer`, and `ConvaiRoomManager` components.
- Full conversation pipeline: Speech Recognition, Language Understanding and Generation, Text-to-Speech, Lipsync.
- Event-driven architecture with `IEventHub` for decoupled communication between SDK components.
- Modular behavior system (`ConvaiCharacterBehaviorBase`, `ConvaiPlayerBehaviorBase`) for extending character and player logic.
- Vision module for camera and webcam frame capture with configurable resolution and frame rate.
- Narrative Design module for trigger-based story progression and section synchronization.
- Scene metadata system (`ConvaiObjectMetadata`, `ConvaiSceneMetadataCollector`) for environment-aware AI.
- Configurable logging framework with pluggable sinks (Console, File, HTTP).
- UI components: transcript display (chat and subtitle modes), connection status indicator, notification system.
- Native transport layer powered by LiveKit for low-latency audio/video streaming.
- REST API client for character management, animation, long-term memory, and narrative services.
- Platform support for Windows, macOS, Linux, Android, and iOS (WebGL support planned).
- Editor tooling: Project Settings panel, custom inspectors, and menu items for quick setup.
- Sample scene with demo characters, animations, and interaction controller.
