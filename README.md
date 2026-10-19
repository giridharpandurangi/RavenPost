# Raven Post (v1.0)

A Valheim mod: build Raven Perches at your bases, and ravens carry items between them.
Works in single player, hosted games and dedicated servers. Everyone (server and players) needs the mod.

## Build

```bash
dotnet build -c Release
```

Output: `bin/Release/RavenPost.dll`.

To build **and** copy it into the r2modman profile's plugins folder:

```bash
dotnet build -c Release -p:Deploy=true
```

Paths are set at the top of `RavenPost.csproj` (`ValheimDir`, `ProfileDir`, `ServerDir`). The default
profile is "Testing". Pass `-p:ProfileDir=...` to use another. If the local dedicated server in `ServerDir`
has BepInEx installed, the DLL is copied there too.

To make the Thunderstore package:

```bash
dotnet build -c Release -p:Package=true
```

Output: `dist/RavenPost-<version>.zip`. The store page, changelog, icon and manifest are in `thunderstore/`
(`make_icon.py` redraws the icon). The build stops if `thunderstore/manifest.json` has a different
version from `<Version>` in the project file, so bump both together.

Tip: for testing, make a separate r2modman profile with only Jötunn installed (and ConfigurationManager if
you like), so other mods can't get in the way.

## How to play

1. Hammer → Misc → **Raven Perch** (needs a workbench). Costs 4 Fine Wood, 4 Feathers, 5 Bronze Nails.
   It's a cargo box with a wooden post and a raven sitting on top.
2. Look at the perch and press **N** to name it, e.g. "Homestead".
3. Build a second perch somewhere else and name it.
4. Open a perch (**E**). Put items in the cargo slots (2×2 by default).
5. In the **Raven Post** window next to the cargo, click a destination, then **Send raven**.
   Shortcut without opening it: look at the perch, **J** picks the next destination, **K** sends.
6. **Feed your raven**: look at the perch and press the hotbar number of a food, or click **Feed** in the
   window (uses the best food you carry). A perch stores up to 10 meals; each flight eats the best one.
7. **Raven mail**: on the Send tab click **Write note**, type up to 200 characters and press Enter. The note
   flies with the next raven, with cargo or on its own (a letter alone needs no meal). Letters arrive in
   the destination perch's **Mail** tab: from, day and message, with **Reply** and **Delete**. The hover text
   shows unread letters.
8. **Upgrade the perch** on the **Perch** tab. Each tier follows the biomes: bigger cargo and faster ravens.

   | Tier | Biome | Upgrade cost | Cargo | Speed |
   |---|---|---|---|---|
   | Wooden | Meadows | (build cost) | 2x2 | x1.0 |
   | Bronze | Black Forest | 5 Bronze, 10 Bronze Nails | 3x2 | x1.2 |
   | Iron | Swamp | 10 Iron, 5 Guck | 4x2 | x1.4 |
   | Silver | Mountains | 10 Silver, 4 Wolf Pelt | 5x3 | x1.7 |
   | Black Metal | Plains | 10 Black Metal, 10 Linen Thread | 6x3 | x2.0 |
   | Eitr | Mistlands | 2 Black Core, 10 Eitr, 10 Carapace | 7x4 | x2.5 |
   | Flametal | Ashlands | 10 Flametal, 3 Celestial Feathers | 8x4 | x3.0 |
   | Deep North | Deep North | 30 Flametal, 2 Molten Core (until Deep North items exist) | 8x6 | x4.0 |

   Every tier's cost, size and speed is in the config. Unknown items are logged at load.
9. **Letters** (no raven needed): craft at the workbench, 1 Leather Scraps + 1 Coal = 2 Letters. Right-click
   a letter (or press its hotbar key) to write up to 500 characters (type `\n` for a new line); hover it to
   read who wrote it, when, and the text. Keep them as a journal, store them anywhere, or send them as
   cargo. **Keep** in the Mail tab turns a received raven letter into a Letter item.
10. A raven flies off. When it lands you get a message in the top left, wherever you are.
   The items appear in the destination perch the next time that area is loaded.

## Rules

- Flight time: 10 s per 100 m (min 10 s), +50% per 100 weight of cargo. It uses in-game time, so
  sleeping speeds deliveries up and they pause while the game is closed.
- Food speeds ravens up: cooked food by 1 + health / 50 (30 hp = x1.6, 75 hp = x2.5, up to x4), raw meat
  x1.25. Perch speed and meal speed multiply. A hungry raven flies at x0.5, or refuses to fly if `HungryRaven = Refuse`. `UseFood = false`
  ignores food completely.
- Ravens can carry ore and metal by default (`AllowNonTeleportable`).
- If the destination is full, the rest waits in the perch until you make room.
- A perch with deliveries waiting or in flight can't be deconstructed. If it gets destroyed anyway, the
  items drop on the ground there.

All of these are in `BepInEx/config/com.ravenpost.valheim.cfg` after the first launch.

## How it works

- Every perch is a cloned vanilla chest plus a `RavenPerch` component. The chest holds the cargo.
- Deliveries are saved on the **destination perch's own world data** (`ravenpost_inbox`), so they're kept
  in the world save. No extra files.
- Perches are found through the world's object list (`ZDOMan`), which includes areas that aren't loaded.
  Only the server has the full list, so players ask the server for it.
- Multiplayer: the server is the post office. A sent raven goes to the server, which hands the delivery
  to whoever owns the destination perch (the player near it) or stores it itself; if the destination was
  destroyed, it flies back to the sender's perch. The server tells the sender when their raven lands and
  everyone when a letter arrives. Gameplay settings are synced from the server; keybinds stay personal.
- When a perch loads, it moves every arrived delivery into its cargo slots.

## Files

- `src/Plugin.cs`: config, piece registration, keys
- `src/RavenPerch.cs`: per-perch logic: send, collect arrivals, hover text, rename
- `src/PostOffice.cs`: finding perches, inbox save format, flight time
- `src/RavenFlight.cs`: take-off and landing animations (visual only)
- `src/PerchModel.cs`: builds the perch look from vanilla models
- `src/PerchTiers.cs`: upgrade tiers, costs, sizes and speeds
- `src/RavenFood.cs`: what ravens eat, meal storage
- `src/LetterItem.cs`: the Letter item: recipe, icon, writing, tooltip
- `src/RavenMail.cs`: letters, mailbox storage, note writing
- `src/SendWindow.cs`: the perch window: Send tab (destinations, note, feed, send) and Mail tab
- `src/RavenNet.cs`: multiplayer messages: perch list, deliveries, notifications, take-off visuals
- `src/Patches.cs`: Harmony patches for hover text and removal

## Ideas

| Status | Feature | What it gives you | Effort |
|---|---|---|---|
| Done (v0.2) | Real perch look | Wooden post with a sitting raven next to the cargo box | Medium |
| Done (v0.2) | Send window | Destination list and Send button next to the cargo slots | Medium |
| Done (v0.3) | Raven food | Meals speed ravens up; hungry ravens fly slowly | Small |
| Done (v0.4) | **Raven mail** | Mailbox on every perch: write notes, with or without cargo. Inbox shows from, day and message; read, delete, reply. Hover shows unread count | Medium |
| Done (v0.5) | Perch upgrades | Wooden, Bronze, Iron tiers: more cargo slots, faster ravens, longer range | Small–Medium |
| Done (v0.6) | Letter items | Craftable letters that keep their text, can be stored and collected | Medium |
| Done (v0.7) | Multiplayer | Send cargo and mail to friends' perches on a server | Large |
| Ready (v1.0) | Publish on Thunderstore | Icon, manifest and description so others can install it with r2modman | Small |
