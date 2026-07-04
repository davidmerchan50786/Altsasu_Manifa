# Convai Lip Sync Module

This module maps Convai lip-sync transport data to blendshape playback through profile assets, mapping assets, and a runtime playback component.

## Use this when

- you want characters to animate from Convai lip-sync data
- you need profile-specific mappings such as ARKit, MetaHuman, CC4, or Ready Player Me
- you need editor tooling for mapping, validation, or runtime inspection

## Main pieces

- `ConvaiLipSyncComponent` — runtime bridge from Convai lip-sync events to blendshape playback
- `ConvaiLipSyncProfileAsset` — profile metadata identified by stable string IDs
- `ConvaiLipSyncMapAsset` — source-to-target mapping for a profile
- `LipSyncProfileCatalog` — runtime lookup of known profiles

## Runtime model

- profile selection is string-ID based, not enum-based
- `ConvaiLipSyncComponent` resolves its effective profile from an explicit component lock or the character-level desired profile
- mapping resolution prefers an explicit map on the component and falls back to the registered default map for that profile
- unsupported profile IDs or invalid transport payloads fail closed
- WebGL can begin lip-sync playback once browser audio is active, even if a native-style playback-start event is not emitted

## Built-in profiles

Built-in profile assets ship under:

- `Resources/LipSync/Profiles`
- `Resources/LipSync/ProfileRegistries/LipSyncBuiltInProfileRegistry.asset`

Current built-in IDs:

- `arkit`
- `metahuman`
- `cc4_extended`
- `cc4_standard`
- `readyplayerme`

## Editor surfaces

- `ConvaiLipSyncComponentEditor`
- `ConvaiLipSyncMapAssetEditor`
- `ConvaiLipSyncMapDebugWindow`
- `ConvaiLipSyncProfileAssetEditor`

These editors validate unknown profile IDs, duplicate catalog entries, missing default maps, and invalid profile transport configuration.

## Go deeper

- Runtime component: `Components/ConvaiLipSyncComponent.cs`
- Mapping asset: `Assets/ConvaiLipSyncMapAsset.cs`
- Profile catalog: `Profiles/LipSyncProfileCatalog.cs`
- Acceptance template: `Docs/LipSyncAcceptanceReport.md`
