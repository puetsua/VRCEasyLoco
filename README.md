# EasyLoco

* [English](README.md)
* [正體中文](README.zh-hant-TW.md)

Customise your VRChat avatar's locomotion — idle poses, sleeping, and AFK — without hand-editing
animator controllers. Installs through Modular Avatar.

## Features

* **Idle poses** — swap the standing, crouching, and prone idle animations, and register extra
  poses that you can switch between from the expression menu at runtime.
* **Sleeping** *(a separate module — its own button, its own prefab)* — a Sleep sub-menu with two
  toggles: *Sleep Loco* plays a sleeping pose while prone (three clips blend by head orientation —
  facing up, facing down, on your side — detected from real head tracking, and locomotion keeps
  working so you can still crawl), and *Feet Lock* pins both feet to the pose so your standing legs
  don't drag it around. Both release automatically when you stand up. It appends over whatever base
  locomotion the avatar already has, so you can install just sleeping — with or without the rest of
  EasyLoco.
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

Sleeping is a module of its own and **Build Modular Avatar** does not touch it. Use
**Build and append Sleep Locomotion**, at the top of the Module - Sleep Animations section: it builds
the sleeping prefab with the clips you set and appends it to the avatar. Once it is on, the same
button reads **Remove Sleep Locomotion** and takes it back off; to pick up a clip change, remove and
build again.

Sleeping goes inside `GeneratedEasyLocoMA`, so everything EasyLoco installs sits under one object,
and its menu entry nests under `EasyLoco`. On an avatar that has never run the main build there is no
such object, so it goes beside the descriptor instead and its entry goes to the root menu — sleeping
is usable on an avatar running nothing else from EasyLoco. Build the main prefab after sleeping and
you will want to press the sleep button again, which moves it into place.

Because it goes on as an added child of the `GeneratedEasyLocoMA` instance, it survives rebuilding
that prefab but does not travel with it: dropping `GeneratedEasyLocoMA.prefab` onto another avatar
brings the locomotion, not sleeping. Drop `EasyLocoSleep.prefab` alongside it for that.

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
