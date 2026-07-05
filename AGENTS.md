# AGENTS.md

Guidance for Codex when working in `vrchat.puetsuaworkshop.easyloco`.

## What this is

`vrchat.puetsuaworkshop.easyloco` is a Unity Editor-only VPM/UPM package for VRChat avatar locomotion editing. It should help users inspect, adjust, and eventually generate safer locomotion controller changes without hand-editing Animator Controller assets.

The package is developed inside the parent Unity project at `C:\Data\Projects\Unity\VRCAvatarTool`, but this folder is its own nested git repository. Commits made here belong to the EasyLoco package repository.

## Build and validation

There is no CLI build or test suite yet. Unity imports and compiles the package through the editor assembly definition in `Editor/`.

Validate changes by opening the parent project in Unity 2022.3.22f1 and using:

- Tools -> EasyLoco
- The sample avatars in `Assets/main.unity`

Do not edit Unity-generated root `.csproj` or `.sln` files.

## Scope

Keep package code under this folder. Do not modify sample avatars, vendored dependencies, or other Puetsua tools unless the requested change explicitly needs it.

EasyLoco should start conservative:

- inspect locomotion-related avatar state before mutating anything
- avoid destructive edits to existing animator controllers
- prefer generated assets or explicit copy/edit flows when adding controller generation
- keep VRChat SDK interactions idempotent and easy to audit

## Conventions

- Namespace: `Puetsua.VRCEasyLoco.Editor`
- Editor entry point: `Tools -> EasyLoco`
- Code lives under `Editor/` until runtime components are truly required
- Constants live in `EasyLocoConst.cs`; do not duplicate package names or menu labels
- Localized UI text should be centralized before the UI grows beyond this initial scaffold

## Release

Releases are intended to mirror the ButtonWizard package:

- bump `version` in `package.json`
- run `.github/workflows/release.yaml` manually
- publish a zip and `.unitypackage`
- notify the `puetsua/vrc-stuff` VPM listing through repository dispatch

