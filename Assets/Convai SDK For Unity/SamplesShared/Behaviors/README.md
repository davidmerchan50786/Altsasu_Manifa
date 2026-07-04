# Behaviors (Sample Pack)

This folder contains small, copy‑pasteable scripts that show how to react to Convai events (speech, transcripts, “ready”) in a modular way.

If you’re integrating the SDK, start with: `Documentation~/SETUP.md`.

## Who should read this

- **Designers / producers** who want to understand what can be driven by AI dialogue (animations, triggers, UI)
- **Engineers** who want a clean way to add “game rules” without forking the SDK

If you want the broader event-system map first, read `Documentation~/WORKING-WITH-EVENTS.md`.

## Why behaviors exist

Convai scenes usually need small bits of glue code:

- “When the NPC starts speaking, play an animation”
- “When the NPC says a keyword, trigger a quest/shop UI”
- “When the backend says the character is ready, kick off a scripted moment”

The SDK supports this using an **interceptor chain**:

- Behaviors are ordered by **Priority** (higher runs first)
- For callbacks that return `bool`, return:
  - `true` to **consume/intercept** the event (stop the chain)
  - `false` to **observe** the event (let others run)

These behavior scripts are one integration path, not the only one:

- use `ConvaiManager.Events` for typed room/session reactions
- use `ConvaiManager.Transcripts` for transcript state/history
- use relay components for no-code UnityEvent wiring
- use behaviors when you want ordered gameplay logic on top of character or player callbacks

## How to use (Character behaviors)

1. On your NPC GameObject, add:
   - `Convai/Convai Character`
   - `Convai/Character Behavior Dispatcher`
2. Add one or more behavior components (implement `IConvaiCharacterBehavior`).
   - Recommended base class: `ConvaiCharacterBehaviorBase`
3. Set each behavior’s **Priority** field (higher runs earlier).

If you’re unsure this is wired correctly, open the sample scene:

- `Samples/BasicSample/Scenes/Basic Sample.unity` (after importing samples via Package Manager)

## What’s included

### Character behaviors (wired via `CharacterBehaviorDispatcher`)

- `SpeechAnimationBehavior`
  - Sets an Animator bool parameter named `IsSpeaking` on speech start/stop.
- `ShopkeeperBehavior`
  - Looks for commerce keywords in the **final** transcript and calls `agent.SendTrigger(...)`.
  - Because it returns `true` when it fires, it **consumes** that transcript event for lower-priority behaviors.
- `QuestGiverBehavior`
  - On `OnCharacterReady`, sends a `quest.step` trigger (example of a scripted “start” moment).

### Player-side runtime setup

Scene-level conversation setup now lives on `ConvaiRoomManager`.

- Use `How The Player Talks = Hands Free` for the default smart-turn path.
- Use `How The Player Talks = Push To Talk` to enable the built-in scene keybind flow.
- Use `Room Setup Source = Room Manager Profile Asset` when you want reusable advanced room defaults to drive the scene instead of inline scene defaults.

The supported runtime implementation for push-to-talk is `ConvaiPushToTalkController`, which the manager provisions and drives automatically from the room-manager configuration.

If you are building a custom runtime flow, the advanced low-level control surfaces are still the room connection and audio services:

- `SetSttMuted(bool)`
- `ForceUserStoppedSpeaking()`
- local mic mute controls via `ConvaiManager.Audio.ToggleMicMuted()`

### Test doubles

The `Test*` scripts are used by edit-mode tests and are not intended for production.

## Common pitfalls / gotchas

- Behaviors must live on the **same GameObject** as the dispatcher and `ConvaiCharacter`.
- Start by returning `false` (observe) until you know you need to intercept.
- `agent.SendTrigger(...)` only helps if your Convai backend/project is set up to respond to that trigger name.
- Many rules should only run on **final** transcripts (interim results can change).

## Go deeper

- Setup + where to add components: `Documentation~/SETUP.md`
- Behavior system types:
  - `SDK/Runtime/Components/CharacterBehaviorDispatcher.cs`
  - `SDK/Runtime/Behaviors/Character/IConvaiCharacterBehavior.cs`
