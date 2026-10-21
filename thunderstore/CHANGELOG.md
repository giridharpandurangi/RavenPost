# Changelog

## 1.1.0

Two optional balance settings (both off by default, set by the server in multiplayer):

- `MaxCargoWeight`: the heaviest cargo a raven will carry in one flight, for example 500.
- `MaxFlightsPerPeriod` + `PeriodDays`: how many ravens each perch can send per in-game day (or per
  few days). A letter on its own doesn't count unless `LettersCountAsFlights` is on.

The window and hover text show the weight limit and flights left.

## 1.0.0

First public release.

- Raven Perches: send cargo between your bases, delivered even when nobody is at the destination.
- Sitting raven on a wooden post; take-off and landing animations that follow the real flight time.
- Send window with destination list, plus J / K / N shortcuts.
- Raven food: meals speed ravens up by the food's health; hungry ravens fly slowly (configurable).
- Eight perch tiers from Wooden to Deep North: bigger cargo and faster ravens.
- Raven mail: notes with or without cargo, a Mail tab with Reply, Keep and Delete.
- Letters: craftable items you write on and read from the tooltip; only the author can change them.
- Multiplayer: the server tracks perches and deliveries, notifies players, and syncs gameplay settings.
