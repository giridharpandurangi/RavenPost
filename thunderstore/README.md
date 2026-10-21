# Raven Post

Build **Raven Perches** at your bases and let ravens carry your cargo and letters between them. Feed them
well and they fly faster. Upgrade perches through the biomes for bigger cargo holds and quicker ravens.
Write letters, keep a journal, and send mail to friends on a server.

Works in **single player, hosted games and dedicated servers**.

## Features

- **Raven Perch**: a cargo box with a wooden post and a sitting raven. Name each perch, pick a
  destination from a list, and press **Send raven**. The raven takes off, flies, and lands at the other
  perch, even if nobody is there: the cargo waits until you arrive.
- **Ore and metal allowed**: ravens carry what portals won't (can be turned off).
- **Raven food**: feed meals from your hotbar or with the **Feed** button. Better food flies faster:
  speed is 1 + the food's health ÷ 50 (cooked meat x1.6, end-game food x3+). A hungry raven still flies,
  just slowly (or refuses, if you prefer).
- **Perch upgrades**: eight tiers following the biomes, upgraded in place:

  | Tier | Upgrade cost | Cargo | Speed |
  |---|---|---|---|
  | Wooden (Meadows) | build cost | 2x2 | x1.0 |
  | Bronze (Black Forest) | 5 Bronze, 10 Bronze Nails | 3x2 | x1.2 |
  | Iron (Swamp) | 10 Iron, 5 Guck | 4x2 | x1.4 |
  | Silver (Mountains) | 10 Silver, 4 Wolf Pelt | 5x3 | x1.7 |
  | Black Metal (Plains) | 10 Black Metal, 10 Linen Thread | 6x3 | x2.0 |
  | Eitr (Mistlands) | 2 Black Core, 10 Eitr, 10 Carapace | 7x4 | x2.5 |
  | Flametal (Ashlands) | 10 Flametal, 3 Celestial Feathers | 8x4 | x3.0 |
  | Deep North | 30 Flametal, 2 Molten Core | 8x6 | x4.0 |

  Perch speed and food speed multiply. The Deep North tier uses Ashlands materials until Deep North
  items are in the game; change its cost in the config any time.
- **Raven mail**: write a note (up to 200 characters) and send it with or without cargo. Each perch has
  a **Mail** tab: from, day and message, with **Reply**, **Keep** and **Delete**. A letter alone needs no meal.
- **Letters**: craft at the workbench (1 Leather Scraps + 1 Coal = 2 Letters). Right-click to write up
  to 500 characters, hover to read. Keep them as a journal, store them anywhere, or send them as cargo.
  Only the author can change a letter.

## How to use

1. Hammer → **Misc** → **Raven Perch** (needs a workbench): 4 Fine Wood, 4 Feathers, 5 Bronze Nails.
2. Look at it and press **N** to name it. Build a second perch somewhere else.
3. Open a perch (**E**). The **Raven Post** window appears next to the cargo:
   - **Send**: choose a destination, write a note, feed, send.
   - **Mail**: letters received at this perch.
   - **Perch**: tier, meals and the next upgrade.
4. Shortcuts while looking at a perch: **J** next destination, **K** send, **N** rename.

Flight time: 10 seconds per 100 m (at least 5 s), slower with heavy cargo, faster with food and upgrades.
It uses in-game time, so sleeping speeds deliveries up.

## Multiplayer

- **Everyone needs the mod**, the server too, with the same version (Jötunn checks this when you join).
- The server keeps track of all perches and deliveries; you get a message when your raven lands, and
  everyone hears when a letter arrives.
- Gameplay settings come from the server; keybinds stay personal.
- A perch with ravens on the way can't be removed, so nothing gets lost.

## Configuration

`BepInEx/config/com.ravenpost.valheim.cfg`, or in game with the Configuration Manager (F1):
flight speed, cargo rules, food (on/off, hungry behaviour, speeds, raw meats), every tier's cost, size
and speed, and the keys.

**Balance options** (off by default):

- `Cargo / MaxCargoWeight`: heaviest cargo per flight, for example `500`. `0` = no limit.
- `Limits / MaxFlightsPerPeriod`: ravens each perch can send per period, for example `5`. `0` = unlimited.
- `Limits / PeriodDays`: period length in in-game days (`1` = per day; sleeping counts).
- `Limits / LettersCountAsFlights`: whether a letter on its own uses up a flight (off by default).

## Compatibility

- Made for Valheim 1.0.20 with BepInExPack 5.4.2351 and Jötunn 2.30.2.
- Mods that resize **all** containers may also resize perches. Perches set their own size per tier.

## Credits

Made by **Faasnu**. MIT license.
