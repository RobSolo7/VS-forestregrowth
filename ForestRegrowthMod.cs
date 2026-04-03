using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ForestRegrowth
{
    /// <summary>
    /// Forest Regrowth Mod
    /// 
    /// Forest floor blocks periodically attempt to spawn a sapling on top of themselves
    /// if there are no trees (logs) or saplings within a 15-block horizontal radius.
    /// The sapling type is chosen based on local climate data — the same data the game
    /// uses during world generation.
    ///
    /// To permanently stop regrowth in an area, dig up the forest floor blocks.
    /// </summary>
    public class ForestRegrowthMod : ModSystem
    {
        private ICoreServerAPI sapi;

        // How often (in real milliseconds) we run the per-player scan sweep.
        // 60 seconds means each forest floor block near a player is checked once per minute on average.
        private const int TickIntervalMs = 5_000;

        // Radius (in blocks) to check for existing trees/saplings before spawning.
        private const int ClearRadius = 15;

        // Chance (0.0 – 1.0) that an eligible forest floor block actually spawns a sapling per tick.
        // With a 60 s tick this gives roughly one sapling per ~20 minutes per eligible block on average.
        private const double SpawnChance = 1.0;

        // How many forest floor blocks we sample around each loaded player per tick.
        // Keeps performance reasonable on busy servers.
        private const int SamplesPerPlayerPerTick = 8;

        // Horizontal range around a player we sample from.
        private const int ScanRange = 48;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            sapi.Event.Timer(OnTick, TickIntervalMs / 1000.0); // Timer takes seconds
        }

        private void OnTick()
        {
            IServerPlayer[] players = sapi.World.AllOnlinePlayers as IServerPlayer[];
            if (players == null || players.Length == 0) return;

            Random rng = sapi.World.Rand;

            foreach (IServerPlayer player in players)
            {
                if (player.ConnectionState != EnumClientState.Playing) continue;

                BlockPos playerPos = player.Entity?.Pos?.AsBlockPos;
                if (playerPos == null) continue;

                for (int i = 0; i < SamplesPerPlayerPerTick; i++)
                {
                    // Pick a random offset within ScanRange
                    int dx = rng.Next(-ScanRange, ScanRange + 1);
                    int dz = rng.Next(-ScanRange, ScanRange + 1);

                    // Find the surface at that (x,z)
                    int x = playerPos.X + dx;
                    int z = playerPos.Z + dz;
                    int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z));

                    BlockPos surfacePos = new BlockPos(x, y, z);
                    Block surfaceBlock = sapi.World.BlockAccessor.GetBlock(surfacePos);

                    // Only act on forest floor blocks
                    if (!IsForestFloor(surfaceBlock)) continue;

                    // The block on top must be air
                    BlockPos abovePos = surfacePos.UpCopy();
                    Block aboveBlock = sapi.World.BlockAccessor.GetBlock(abovePos);
                    if (aboveBlock == null || aboveBlock.BlockId != 0) continue;

                    // Probability check
                    if (rng.NextDouble() > SpawnChance) continue;

                    // Check for nearby trees/saplings
                    if (HasNearbyTreeOrSapling(surfacePos)) continue;

                    // Pick sapling based on climate
                    Block saplingBlock = ChooseSapling(surfacePos);
                    if (saplingBlock == null) continue;

                    // Place the sapling on top of the forest floor
                    sapi.World.BlockAccessor.SetBlock(saplingBlock.BlockId, abovePos);
                    sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(abovePos);
                    Mod.Logger.Notification($"[ForestRegrowth] Spawned {saplingBlock.Code.Path} at ({abovePos.X}, {abovePos.Y}, {abovePos.Z})");
                }
            }
        }

        private bool IsForestFloor(Block block)
        {
            // Forest floor blocks have codes like "game:forestfloor-1", "game:forestfloor-2", etc.
            return block?.Code?.Domain == "game" &&
                   block.Code.Path.StartsWith("forestfloor");
        }

        private bool HasNearbyTreeOrSapling(BlockPos centerPos)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos checkPos = new BlockPos();

            for (int dx = -ClearRadius; dx <= ClearRadius; dx++)
            {
                for (int dz = -ClearRadius; dz <= ClearRadius; dz++)
                {
                    // Circular radius check
                    if (dx * dx + dz * dz > ClearRadius * ClearRadius) continue;

                    // Check a vertical slice from surface-2 to surface+24
                    for (int dy = -2; dy <= 24; dy++)
                    {
                        checkPos.Set(centerPos.X + dx, centerPos.Y + dy, centerPos.Z + dz);
                        Block b = ba.GetBlock(checkPos);
                        if (b == null || b.BlockId == 0) continue;

                        string path = b.Code?.Path ?? "";

                        // Tree logs
                        if (path.StartsWith("log-") || path.StartsWith("aged-log-"))
                            return true;

                        // Sapling blocks
                        if (path.StartsWith("sapling-"))
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Choose a sapling type using the climate at the given position,
        /// mirroring how the game picks trees during worldgen.
        /// Falls back to oak if nothing matches.
        /// </summary>
        private Block ChooseSapling(BlockPos pos)
        {
            ClimateCondition climate = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
            if (climate == null) return null;

            // Build a prioritized list of candidates matching the climate.
            // Temperature in VS worldgen is roughly: <5 = very cold, 5-12 = cold, 12-20 = temperate, >20 = warm.
            // Rainfall: 0.0–1.0 normalized.
            // These ranges approximate vanilla worldgen tree selection.

            var candidates = new List<string>();

            float temp = climate.Temperature;
            float rain = climate.Rainfall;

            // Boreal / cold
            if (temp < 8f)
            {
                candidates.Add("pine");
                candidates.Add("larch");
                if (rain > 0.45f) candidates.Add("birch");
                if (temp > 2f) candidates.Add("birch");
            }

            // Temperate
            if (temp >= 6f && temp < 18f)
            {
                candidates.Add("oak");
                if (rain > 0.5f) candidates.Add("maple");
                if (rain > 0.55f) candidates.Add("hornbeam");
                candidates.Add("birch");
                if (temp > 10f) candidates.Add("walnut");
            }

            // Warm / subtropical
            if (temp >= 16f)
            {
                candidates.Add("acacia");
                if (rain > 0.55f) candidates.Add("kapok");
                if (rain > 0.6f) candidates.Add("baldcypress");
                candidates.Add("oak");
            }

            // Always available as a universal fallback
            candidates.Add("oak");

            // Shuffle candidates and find the first one that exists in the game
            Shuffle(candidates, sapi.World.Rand);
            foreach (string tree in candidates)
            {
                // Sapling codes are like "game:sapling-oak-free"
                // "free" means the sapling can grow in open air (no soil block requirement variant)
                Block b = sapi.World.GetBlock(new AssetLocation("game", $"sapling-{tree}-free"));
                if (b != null && b.BlockId != 0) return b;

                // Some trees don't have a -free variant; try without suffix
                b = sapi.World.GetBlock(new AssetLocation("game", $"sapling-{tree}"));
                if (b != null && b.BlockId != 0) return b;
            }

            return null;
        }

        private static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int n = list.Count - 1; n > 0; n--)
            {
                int k = rng.Next(n + 1);
                T tmp = list[k];
                list[k] = list[n];
                list[n] = tmp;
            }
        }
    }
}
