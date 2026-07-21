# Getting Started

EasyLoco replaces your avatar's Base and Action locomotion with a customisable set of idle, sleep,
and AFK animations, installed through Modular Avatar.

## Install onto an avatar

1. Select your avatar (the GameObject holding the `VRCAvatarDescriptor`).
2. `GameObject -> Add EasyLoco Component`, or add the `EasyLoco` component manually.
3. Configure the animations you want (see below). Every field is prefilled with a built-in default,
   so you can skip straight to step 4.
4. Press **Build Modular Avatar**.

Building creates a `GeneratedEasyLocoMA` child on your avatar holding the Modular Avatar
components, and writes generated controllers and menus to
`Assets/PuetsuaWorkshop/Generated/EasyLoco/<avatar name>/`.

Rebuilding is safe and idempotent — press the button again after any change. Nothing outside the
generated folder and the `GeneratedEasyLocoMA` object is modified, and your avatar's existing
animator layers are left untouched.

## Idle Animations

Each stance (Stand / Crouch / Prone) takes a list of idle poses. Row 0 is the **Default** pose: its
clip can be replaced but the row itself cannot be removed.

Add more rows to expose a pose picker in the expression menu under `EasyLoco -> Idle Poses`. A
stance with only one pose gets no menu entry — the single clip is simply used as that stance's idle.

## Sleep Animations

Toggling `EasyLoco -> Sleep` while prone plays a sleeping pose instead of the normal prone idle.
Locomotion still works while asleep, so you can crawl without leaving the pose.

Three clips blend by head orientation:

| Field | Played when |
|---|---|
| Facing Up | lying on your back |
| Facing Down | lying face down |
| On Side | head rolled to either side |

Orientation is detected by a contact rig (`GeneratedEasyLocoMA/SleepLoco`) that compares your head
bone against a world-up reference. Because it reads real head tracking, blending between poses only
behaves correctly in VR — in desktop mode the head is driven by the animation itself, so the
detected orientation will not respond to your input.

Sleep mode releases automatically when you stand up, even if the toggle is still on. It is synced
so remote players see the pose, but not saved — you never rejoin a world already asleep.

## AFK Animations

AFK is branched by posture: Stand, Crouch, and Prone each play their own entering / looping /
exiting clips. Leave any stage empty to keep the built-in default for that branch.

## Notes

- Leaving a clip at its built-in default costs nothing — EasyLoco only generates avatar-specific
  copies of a blend tree when you actually override something inside it.
- The generated folder is disposable. Deleting it and rebuilding produces the same result.
