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

## Fika

Works in Fika co-op: every player's open body is protected from the bots, whether that player is
the host or not. Other players can still loot it.

### Own kills only (host's choice)

In F12, under **THATSMINE Fika → 1. Co-op**, the host can turn on **Players can only loot their own
kills**. Then nobody (the host included) can take items off a body another player killed: you can
open it and look, but nothing moves, and you get a "Not your kill" message naming the killer. The
body opens up to everyone once its killer dies or extracts. The host can switch it during a raid;
everyone else's setting is ignored. With a headless host, set it in the headless client's
`BepInEx/config/com.mybutthasarash.thatsmine.fika.cfg`.

A friend who disconnects without dying or extracting still counts as in the raid, so their kills
stay locked until the raid ends.

### Requirements

**Everyone in the raid needs THATSMINE, the host most of all.** If a player who has it opens a body
in a raid whose host doesn't, the host's game drops that moment's network updates. If you run a
Fika server, add `com.mybutthasarash.thatsmine` and `com.mybutthasarash.thatsmine.fika` to Fika's
required mods so nobody can join without them. With a headless host, install it on the headless
client too.

## Install

Unzip into your SPT folder. That puts `ThatsMine.dll` and `ThatsMine.Fika.dll` in
`BepInEx/plugins`. Without Fika, BepInEx skips `ThatsMine.Fika.dll` and logs that it is missing
Fika; that is expected.

## Build

```powershell
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -FikaCorePath <Fika.Core.dll>            # build + zip into dist\
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -FikaCorePath <Fika.Core.dll> -Install   # also copy into the install
```

`-FikaCorePath` can be left out when Fika is installed in that SPT folder.
