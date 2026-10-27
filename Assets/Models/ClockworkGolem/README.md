# Clockwork Barrel Golem

A Unity-ready mechanical golem inspired by the supplied Lineage 2 reference. The asset is deliberately built from separate named parts so it can be animated and later connected to server-authoritative gameplay.

## Included

- COLLADA model: `ClockworkGolem.dae` (native Unity model import, no extra package required).
- Wood staves, grain accents, four metal hoops with rivets, chest mounting plate and raised hardware.
- Rotating one-eyed helmet with metal brow, vent slits and a warm glowing-looking optic.
- Two independent arms: a spiked maul on the left and a heavy hammer on the right.
- Hinged chest hatch with a raised emblem.
- Cannon concealed behind the hatch; the cannon slide deploys forward when the hatch opens.
- Spoked drive wheel, crank and two foot pedals.
- `ClockworkGolemAnimator.cs` for idle head scanning, pedal rotation, independent arm rolls, hatch opening, cannon deployment and firing/recoil animation.
- `ClockworkGolemPrefabSetup.cs` adds the animator component and saves a ready prefab.

## Import and build

1. Pull the latest changes and open the project in Unity 6. The file is under Assets / Models / ClockworkGolem.
2. Wait until Unity finishes importing the COLLADA file.
3. From the Unity menu choose Tools / Game Client / Build Clockwork Golem Prefab.
4. The ready prefab is written to Assets / Models / ClockworkGolem / ClockworkGolem.prefab. Drag it into a scene.

The front of the model faces local +Z and local +Y is up. One Unity unit is treated as approximately one metre. The model intentionally contains no gameplay colliders or damage logic; add collision shapes and combat integration according to the game's server-authoritative rules.

## Runtime controls

The prefab's ClockworkGolemAnimator component exposes public fields and methods:

- `SetHeadYaw(degrees)`
- `SetLeftArmRoll(degrees)` and `SetRightArmRoll(degrees)`
- `SetChestOpen(true/false)`
- `FireCannon()`

The two arm pivots rotate around their own lengthwise axis, independently of one another. FireCannon animates the hatch, cannon extension and recoil; projectile spawning and damage should stay in the game's combat system.

## Animation pivots

The imported hierarchy intentionally retains these node names:

- `HeadPivot`
- `LeftArmPivot`
- `RightArmPivot`
- `ChestDoorPivot`
- `CannonSlide`
- `PedalDrivePivot`
- `Wheel`

All geometry and materials are original procedural content made for this project, using the uploaded image as a design reference rather than copying extracted game assets.
