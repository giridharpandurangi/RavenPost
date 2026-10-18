# Raven Post (v0.3)

A Valheim mod: build Raven Perches at your bases, and ravens carry items between them.
Single player only for now.

## Build

```bash
dotnet build -c Release
```

Output: `bin/Release/RavenPost.dll`.

To build **and** copy it into the r2modman profile's plugins folder:

```bash
dotnet build -c Release -p:Deploy=true
```

Paths are set at the top of `RavenPost.csproj` (`ValheimDir`, `ProfileDir`). The default profile is
"Testing". Pass `-p:ProfileDir=...` to use another.

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
7. A raven flies off. When it lands you get a message in the top left, wherever you are.
   The items appear in the destination perch the next time that area is loaded.

## Rules

- Flight time: 10 s per 100 m (min 10 s), +50% per 100 weight of cargo. It uses in-game time, so
  sleeping speeds deliveries up and they pause while the game is closed.
- Food speeds ravens up: cooked food by its health value (30 hp = x1.2, 60 = x1.4, 90+ = x1.6), raw meat
  x1.25. A hungry raven flies at x0.5, or refuses to fly if `HungryRaven = Refuse`. `UseFood = false`
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
  Only the host has the full list, so v0.1 turns itself off when you join someone else's server.
- When a perch loads, it moves every arrived delivery into its cargo slots.

## Files

- `src/Plugin.cs`: config, piece registration, keys
- `src/RavenPerch.cs`: per-perch logic: send, collect arrivals, hover text, rename
- `src/PostOffice.cs`: finding perches, inbox save format, flight time
- `src/RavenFlight.cs`: take-off and landing animations (visual only)
- `src/PerchModel.cs`: builds the perch look from vanilla models
- `src/RavenFood.cs`: what ravens eat, meal storage
- `src/SendWindow.cs`: destination list and Send button next to the cargo slots
- `src/Patches.cs`: Harmony patches for hover text and removal

## Ideas

| Status | Feature | What it gives you | Effort |
|---|---|---|---|
| Done (v0.2) | Real perch look | Wooden post with a sitting raven next to the cargo box | Medium |
| Done (v0.2) | Send window | Destination list and Send button next to the cargo slots | Medium |
| Done (v0.3) | Raven food | Meals speed ravens up; hungry ravens fly slowly | Small |
| Next | **Raven mail** | Mailbox on every perch: write notes, with or without cargo. Inbox shows from, day and message; read, delete, reply. Hover shows unread count | Medium |
| Idea | Perch upgrades | Wooden, Bronze, Iron tiers: more cargo slots, faster ravens, longer range | Small–Medium |
| Idea | Route danger | Storms or the Plains can delay a raven, rarely lose one (can be turned off) | Small |
| Idea | Letter items | Craftable letters that keep their text, can be stored and collected | Medium |
| Idea | Multiplayer | Send cargo and mail to friends' perches on a server | Large |
| Idea | Publish on Thunderstore | Icon, manifest and description so others can install it with r2modman | Small |
