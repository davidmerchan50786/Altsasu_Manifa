# Convai Samples

These are **optional** scenes, prefabs, and scripts that demonstrate how to use the SDK. Samples are imported via Package Manager and can be removed without breaking the core SDK.

If you’re integrating Convai, start with:

- `Documentation~/SETUP.md`

## Which sample should I open?

| What you’re trying to do | Start here |
|---|---|
| Get a native “hello world” scene working | `Samples/BasicSample/Scenes/Basic Sample.unity` |
| Validate browser permission/connect flow (WebGL build) | `Samples/BasicSample/Scenes/Basic Sample.unity` |
| Try the cinematic LipSync showcase | `Samples/LipSyncSample/Scenes/LipSync Sample.unity` |
| Hook transcripts into gameplay triggers/actions | `Behaviors/README.md` |
| Compare the engineer, newcomer, and designer event paths | `Documentation~/WORKING-WITH-EVENTS.md` and `Scripts/Events/` |

## Folder overview

- `SamplesShared/` — shared sample-owned code and future shared sample assets
- `Samples/BasicSample/` — minimal single-character demo and its Basic-only scene assets
- `Samples/LipSyncSample/` — LipSync/URP showcase scene and its sample-local assets
- `Behaviors/` — shared sample `ConvaiCharacterBehaviorBase` / `ConvaiPlayerBehaviorBase` implementations

For WebGL validation, build `Samples/BasicSample/Scenes/Basic Sample.unity` to WebGL and follow the browser gesture/HTTPS requirements in `Documentation~/PLATFORMS.md`.

> **URP note:** The LipSync sample is sample-owned and depends on the Unity Universal Render Pipeline packages. Those dependencies are still kept in the main package manifest for safe sample import/install behavior, even though the real ownership is sample-side.

> **Note:** Reusable UI prefabs (settings panel, transcript UI, notifications) are in the SDK package at `Packages/com.convai.convai-sdk-for-unity/Prefabs/`.

Event-system reference scripts live under `SamplesShared/Scripts/Events/`:

- engineer path: typed `ConvaiManager.Events`
- newcomer path: transcript UI/listener path
- designer path: relay-driven UnityEvent examples

## For developers

Treat these as **reference implementations**. Copy what you need into your project, then customize.

Basic sample uses the shared `Convai.Sample.*` assemblies; LipSync sample is in `Convai.Samples.LipSyncSample`. The sample assemblies:

- references core SDK assemblies
- is **not** referenced by core SDK code (one-way dependency)
- can be removed without affecting the SDK runtime

Sample ownership is intentionally explicit:

- `Samples/BasicSample/` should only own Basic-sample-specific scenes/assets
- `Samples/LipSyncSample/` should only own LipSync-sample-specific scenes/assets
- anything reused across both samples should live in `SamplesShared/` or remain a deliberate core package asset
- Basic and LipSync should not reference each other directly
