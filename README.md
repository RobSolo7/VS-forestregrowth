# Forest Regrowth Mod (v1.1.0)

A performance-optimized, server-side mod for **Vintage Story** that automatically replants saplings on forest floors near players. It uses climate-aware logic to ensure the right trees grow in the right biomes, keeping your world lush and renewable.

## 🌟 Features

* **Climate-Aware Replanting**: Automatically selects tree species (Oak, Pine, Birch, Acacia, Kapok, etc.) based on local temperature and rainfall data.
* **Intelligent Proximity Checks**: Prevents overcrowding by ensuring new saplings are only planted if no other trees or saplings are within a configurable radius.
* **Zero Performance Impact**: Uses an O(1) block ID caching system and randomized sampling to ensure it doesn't lag the server.
* **Live In-Game Config**: Adjust scan ranges, speeds, and toggle features instantly without restarting the server.

## 🛠 Commands

The mod uses a unified `/forest` command system.

| Command | Description |
| :--- | :--- |
| `/forest` | Displays the built-in help menu and a list of all sub-commands. |
| `/forest debug` | **Toggle**: Turns on/off live console notifications whenever a sapling is successfully planted. |
| `/forest inspect` | Technical Tool: Prints block information at your feet to the server log to verify environment detection. |
| `/forest config [setting] [value]` | Updates mod settings on the fly. |
| `/forest version` | Displays the currently installed mod version. |

## ⚙️ Configuration

Settings are saved in `ModConfig/forestregrowth.json`. You can edit the file manually or use the `/forest config` command.

| Setting | Default | Description |
| :--- | :--- | :--- |
| `ClearRadius` | `15` | Minimum distance (in blocks) required between a new sapling and existing trees. |
| `ScanRange` | `80` | Horizontal radius around players where the mod scans for valid ground. |
| `SamplesPerTick` | `64` | Number of random spots checked around each player per cycle. |
| `TickIntervalSeconds` | `10.0` | Frequency of planting attempts. *Requires server restart to apply*. |
| `Enabled` | `true` | Master switch to enable or disable the mod logic. |
| `Verbose` | `false` | When true, every planting event is logged to the server-main.log. |

## 🚀 Technical Requirements

* **Platform**: Server-side only (Clients do not need to install this).
* **Game Version**: Targeted for .NET 8.0 / Modern Vintage Story API.
* **Mod Compatibility**: Works with standard `game:sapling` assets. Compatible with most tree-adding mods that follow vanilla naming conventions.

---
*Created for the Vintage Story Community.*
