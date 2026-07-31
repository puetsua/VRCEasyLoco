# AGENTS.md

Guidance for AI coding agents working in `vrchat.puetsuaworkshop.easyloco`.

## What this is

`vrchat.puetsuaworkshop.easyloco` is a Unity Editor-only VPM/UPM package for VRChat avatar locomotion editing — inspect, adjust, and eventually generate safer locomotion controller changes without hand-editing Animator Controller assets.

Developed inside the parent Unity project at `C:\Data\Projects\Unity\VRCAvatarTool`, but this folder is its own nested git repo. **Commits here belong to the EasyLoco package repo, not the outer project repo** — don't move files across the boundary without checking which repo's history they land in.

## Build and run

No CLI build — Unity compiles the package via the `Editor/` asmdef. Validate in Unity 2022.3.22f1.

EditMode tests (localized dataset contract, idle pose name rules, expression menu pose values, blend-tree cloning, the replacement ledger, and the template contracts — stance transitions in both VRMode branches, and the motion/state names the build writes into): `Window → General → Test Runner → EditMode → Run All`. Assembly `Puetsua.VRCEasyLoco.Editor.Tests`, reaches internals via `InternalsVisibleTo` in `Editor/AssemblyInfo.cs`.

Live-avatar / Modular Avatar / VRChat SDK validation: open the parent project, `GameObject → Add EasyLoco Component` on an avatar, then the inspector's **Build Modular Avatar** button. Sample avatars in `Assets/main.unity` (`Robot`, `nekobot`, `eniisyua5`, `bot1`/`bot2`/`bot3`).

## Project structure

```
Editor/   asmdef: Puetsua.VRCEasyLoco.Editor
  EasyLocoConst.cs           Constants — parameter/layer/menu/prefab names. Refer by name, never hard-code.
  MotionReplacements.cs      Name→Motion lookup ledger; the only way to take a replacement out (see Code style)
  LocalizedTextDataset.cs / .Data.cs   Localized string fields + per-language strings
  AssemblyInfo.cs            InternalsVisibleTo → Tests
Tests/Editor/   EditMode tests (asmdef: Puetsua.VRCEasyLoco.Editor.Tests) — repo-only, excluded from release zip/unitypackage
Runtime/        asmdef: Puetsua.VRCEasyLoco
  EasyLoco.cs                The component the user configures — the serialized pose/AFK/sleep clip
                             lists the build reads, plus `Avatar` (the descriptor on the same
                             GameObject only). `IEditorOnly`, so the VRChat build strips it.
package.json    VPM manifest; `version` is the release trigger
```

`Animations/` `Animators/` `Menus/` `Prefabs/` are template assets the build writes into by name; `Textures/` is package art. `Documentation~/` is maintainer-only — artwork sources and the release procedure — and is on the release workflow's exclusion list; the `~` alone would not keep it out, it only stops Unity importing it. `*.meta` files are Unity-generated GUIDs — keep in sync, never edit by hand.

## Scope

EasyLoco should start conservative:

- inspect locomotion-related avatar state before mutating anything
- avoid destructive edits to existing animator controllers
- prefer generated assets or explicit copy/edit flows when adding controller generation
- keep VRChat SDK interactions idempotent and easy to audit

File-level never-touch rules live in the Boundaries table below.

## Code style

Namespace `Puetsua.VRCEasyLoco.Editor`. Editor entry point: the `EasyLoco` component + its custom inspector (`GameObject → Add EasyLoco Component`). Constants live in `EasyLocoConst.cs` — refer by name, never hard-code.

Replacement lookups go through `MotionReplacements.TryGet` — the ledger that records every name a walk found; `ThrowIfUnmatched` fails the build on any unmatched key.

```csharp
// ✅ Good — take replacements out through the ledger, unmatched names fail the build
if (replacements.TryGet(motionName, out var replacement))
    state.motion = replacement;

// ❌ Bad — bypasses the ledger; a missing name disappears without a word
state.motion = replacementMap.TryGetValue(motionName, out var m) ? m : state.motion;
```

## Conventions

- Every user-facing inspector string lives in `LocalizedTextDataset` (field list and language
  preference in `LocalizedTextDataset.cs`, the strings themselves in `LocalizedTextDataset.Data.cs`),
  reached through the `Localized` shorthand. Adding a string means adding a field and filling it in
  **every** language dataset - there is no per-key fallback, so a missing translation draws blank.
- Expression menu labels are serialized data baked onto the avatar, so they are *synced* (not redrawn). `SyncPoseNames` runs from `OnEnable` and on language switch:
  - row 0 always follows the current language (that inspector field is disabled — no user edit to protect)
  - other rows follow only while their stance is still pristine: same row count, same built-in clips, names matching built-ins in **any** supported language (`LocalizedTextDataset.All`). Pristine-ness is per stance, so editing stand poses must not freeze crouch/prone.
  - Cost: selecting a component can rewrite and dirty it — shared projects with different language prefs see `menuName`s flip in diffs. This is the intended trade-off of per-user language driving shared serialized data. **Do not "fix" it by deleting the sync.**
- Generated asset and file names (`StanceBuild.Key`, `GeneratedAssetPrefix`, controller and menu
  filenames) stay ASCII and language-independent, so a rebuild under another language overwrites the
  previous run's files instead of orphaning them.

## Boundaries

| Tier | Rule |
|------|------|
| ✅ Always | Keep code under this folder; reference constants by name from `EasyLocoConst.cs`; take replacement motions through `MotionReplacements.TryGet`; fill every language dataset when adding a string; keep generated filenames ASCII; run EditMode tests before considering a change done. |
| ⚠️ Ask first | Adding new replacement paths or new localized strings (requires updating every language dataset); schema-level changes to `StatusPlan`-style baked assets; touching anything outside this folder (sample avatars, vendored deps, sibling Puetsua tools). |
| 🚫 Never | Edit Unity-generated root `.csproj` / `.sln` / `*.meta` files by hand; commit secrets or API keys; "fix" the `SyncPoseNames` menu-name flipping by deleting the sync (it is an intentional trade-off); hard-code parameter/layer/menu/prefab names inside merging logic instead of using `EasyLocoConst.cs`; reach around `MotionReplacements` when looking up a motion; move files across the nested-repo boundary without checking which repo they belong to. |

## Release

Maintainer-only. Full procedure (workflow exclusion list, git-cliff changelog rules, preview command) lives in [`Documentation~/release.md`](Documentation~/release.md). Short version: bump `version` in `package.json`, run `.github/workflows/release.yaml` manually, write commit subjects in the imperative (`Add`/`Fix`/`Remove`).