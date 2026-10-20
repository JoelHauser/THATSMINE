# THATSMINE

Client plugin for SPT 4.1.x. While you have a body's loot screen open, AI can't take anything off
that body: not its weapons, armor, rig, backpack, or anything inside them. Close the screen and AI
can loot the body again.

- Protects only the body whose loot screen you have open. Other bodies and other AI behaviour are
  unchanged.
- Covers AI that was already walking to the body or kneeling over it when you opened it.
- By default it applies only in scav raids, and only to bodies you killed. Both are config options.
- Protection ends when the screen closes, when you die, and when the raid ends.

Works with vanilla bot looting and with ORBIT's looting. SAIN doesn't loot bodies itself.

## Install

Unzip into your SPT folder. That puts `ThatsMine.dll` in `BepInEx/plugins`.

## Build

```powershell
scripts\pack.ps1 -SPTPath H:\SPT4.1.X            # build + zip into dist\
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install   # also copy into the install
```
