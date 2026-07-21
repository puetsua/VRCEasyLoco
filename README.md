# EasyLoco

* [English](README.md)
* [正體中文](README.zh-hant-TW.md)

Customise your VRChat avatar's locomotion — idle poses, sleeping, and AFK — without hand-editing
animator controllers. Installs through Modular Avatar.

## Features

* **Idle poses** — swap the standing, crouching, and prone idle animations, and register extra
  poses that you can switch between from the expression menu at runtime.
* **Sleeping** — toggle a sleeping pose while prone. Three clips blend by head orientation (facing
  up, facing down, on your side), detected from real head tracking. Locomotion keeps working, so
  you can still crawl. Releases automatically when you stand up.
* **AFK** — per-posture AFK animations, with separate entering / looping / exiting clips for
  standing, crouching, and prone.

Every animation ships with a built-in default, so it works out of the box.

## Usage

1. Select your avatar and choose `GameObject -> Add EasyLoco Component`.
2. Set the animations you want in the Inspector.
3. Press **Build Modular Avatar**.

The build produces a self-contained `GeneratedEasyLocoMA` prefab and installs it on your avatar.
Rebuilding overwrites that prefab in place, so the installed instance updates with it. You can also
drop the prefab onto a similar avatar by hand to reuse one build.

See [Getting Started](Documentation~/getting-started.md) for the full walkthrough.

## Installation

### VCC

1. Open VRChat Creator Companion.
2. Add the Pue-Tsua Workshop VPM listing.
3. Open your avatar project in VCC.
4. Add `EasyLoco` to the project.

### Unity Package Manager

1. Open Unity Package Manager.
2. Click `+`.
3. Select `Add package from git URL...`.
4. Enter `https://github.com/puetsua/VRCEasyLoco.git`.
