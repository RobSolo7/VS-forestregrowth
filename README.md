# Forest Regrowth
**Vintage Story 1.21.x | Server-side only**

## What it does

Forest floor blocks periodically attempt to spawn a sapling on top of themselves,
provided there are **no trees or saplings within a 15-block radius**.

The sapling type is chosen using the local climate at that position — the same temperature
and rainfall data the game uses during world generation — so oak won't spontaneously appear
in a boreal pine forest.

Regrowth happens across all **server-loaded chunks**, not just near players. This means
forests can recover in the background even when no one is standing nearby.

**To permanently prevent regrowth: dig up the forest floor blocks.** If the underlying
soil is regular dirt or grass, nothing will ever spawn there again.

## Mechanics summary

| Property | Value |
|---|---|
| Target blocks | `game:forestfloor-*` |
| Clear radius | 15 blocks (checks for logs & saplings) |
| Spawn chance per tick per sample | 5% |
| Tick interval | 60 seconds |
| Samples per tick | 64 (spread across all loaded chunks) |
| Climate-aware | Yes — uses worldgen climate values |
| Works while AFK / offline areas | Yes — uses server chunk load distance |

## How to build

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Set the `VINTAGE_STORY` environment variable to your game install directory, e.g. in PowerShell:
   ```powershell
   $env:VINTAGE_STORY = "C:\Users\yourname\AppData\Roaming\Vintagestory"
   ```
3. Run:
   ```
   dotnet build -c Release
   ```
4. The output DLL will be in `bin/Release/net8.0/forestregrowth.dll`.

## Installing the mod

Create a zip file containing:
```
forestregrowth.zip
├── modinfo.json
├── forestregrowth.dll          ← built output
└── assets/
    └── forestregrowth/
        └── lang/
            └── en.json
```

Place the zip in your `Mods/` folder (`%appdata%\VintagestoryData\Mods` on Windows).

## Tuning

All tuning constants are at the top of `ForestRegrowthMod.cs`:

| Constant | Default | Description |
|---|---|---|
| `TickIntervalSeconds` | `60.0` | How often (in real seconds) the sweep runs |
| `SpawnChance` | `0.05` | Probability per eligible sample per tick (5%) |
| `ClearRadius` | `15` | How far to look for existing trees/saplings |
| `SamplesPerTick` | `64` | How many positions are sampled across loaded chunks per tick |

Raise `SamplesPerTick` for faster forest recovery. Lower it to reduce server load on large servers with many loaded chunks.
