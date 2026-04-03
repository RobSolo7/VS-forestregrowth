# Forest Regrowth
**Vintage Story 1.21.x | Server-side only**

## What it does

Forest floor blocks periodically attempt to spawn a sapling on top of themselves,
provided there are **no trees or saplings within a 15-block radius**.

The sapling type is chosen using the local climate at that position — the same temperature
and rainfall data the game uses during world generation — so oak won't spontaneously appear
in a boreal pine forest.

**To permanently prevent regrowth: dig up the forest floor blocks.** If the underlying
soil is regular dirt or grass instead, nothing will ever spawn there again.

## Mechanics summary

| Property | Value |
|---|---|
| Target blocks | `game:forestfloor-*` |
| Clear radius | 15 blocks (checks for logs & saplings) |
| Spawn chance per minute per block | ~5% |
| Sapling placement | 1 block above the forest floor |
| Climate-aware | Yes — uses worldgen climate values |

## How to build

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Set the `VINTAGE_STORY` environment variable to your game install directory
   (e.g. `C:\Program Files\Vintagestory`), **or** edit the `<GamePath>` fallback
   in `forestregrowth.csproj`.
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

Place the zip in your `Mods/` folder (inside your VintagestoryData directory).

## Notes & tuning

All tuning constants are at the top of `ForestRegrowthMod.cs`:

- `TickIntervalMs` — how often (in real milliseconds) the sweep runs. Default: 60 000 (1 minute).
- `SpawnChance` — probability per eligible block per tick. Default: 0.05 (5%).
- `ClearRadius` — how far to look for existing trees/saplings. Default: 15.
- `SamplesPerPlayerPerTick` — how many blocks to sample around each player per tick. Default: 8.
- `ScanRange` — horizontal range around each player to sample from. Default: 48.

Lowering `SpawnChance` or raising `TickIntervalMs` makes forests recover more slowly.
Raising `ClearRadius` makes it harder for saplings to appear near any surviving trees.
