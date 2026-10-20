# THATSMINE

Client plugin for SPT 4.1.x, made for solo play. While you have a body's loot screen open, AI can't
take anything off that body: not its weapons, armor, rig, backpack, or anything inside them. Close
the screen and AI can loot the body again.

- Protects only the body whose loot screen you have open. Other bodies and other AI behaviour are
  unchanged.
- Covers AI that was already walking to the body or kneeling over it when you opened it.
- By default it applies only in scav raids, and only to bodies you killed. Both are config options.
- Protection ends when the screen closes, when you die, and when the raid ends.

Works with vanilla bot looting and with ORBIT's looting. SAIN doesn't loot bodies itself.

## Install

Unzip `THATSMINE-<version>.zip` into your SPT folder. That puts `ThatsMine.dll` in
`BepInEx/plugins`. That is everything you need for solo play; Fika is not required.

## Playing with Fika (optional)

Only if you play Fika co-op: also unzip `THATSMINE-Fika-<version>.zip`, which adds
`ThatsMine.Fika.dll`. Don't install it without Fika: it needs Fika to load, and without it SPT
reports a plugin that failed to load.

With the add-on, every player's open body is protected from the bots, whether that player is the
host or not. Other players can still loot it.

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

**Everyone in the raid needs both zips, the host most of all.** If a player who has them opens a
body in a raid whose host doesn't, the host's game drops that moment's network updates. If you run
a Fika server, add `com.mybutthasarash.thatsmine` and `com.mybutthasarash.thatsmine.fika` to Fika's
required mods so nobody can join without them. With a headless host, install them on the headless
client too.

## Build

```powershell
scripts\pack.ps1 -SPTPath H:\SPT4.1.X                                          # solo zip into dist\
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -FikaCorePath <Fika.Core.dll>            # solo zip + Fika add-on zip
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -FikaCorePath <Fika.Core.dll> -Install   # also install
```

`-FikaCorePath` can be left out when Fika is installed in that SPT folder. `-Install` only installs
the Fika add-on into an SPT folder that has Fika.
