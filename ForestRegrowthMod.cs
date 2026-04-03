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
    /// Each tick, samples random positions across all currently loaded chunks.
    /// If a forest floor block is found with no trees or saplings within 15 blocks,
    /// a climate-appropriate sapling is spawned on top of it.
    ///
    /// To permanently prevent regrowth, dig up the forest floor blocks.
    /// </summary>
    public class ForestRegrowthMod : ModSystem
    {
        private ICoreServerAPI sapi = null!;

        // How often (in real seconds) the sweep runs.
        private const double TickIntervalSeconds = 60.0;

        // Radius (in blocks) to check for existing trees/saplings before spawning.
        private const int ClearRadius = 15;

        // Chance (0.0 – 1.0) that an eligible forest floor block spawns a sapling per tick.
        private const double SpawnChance = 0.05;

        // How many positions we sample across all loaded chunks per tick.
        // Raise this if forests feel too slow to recover; lower it to reduce server load.
        private const int SamplesPerTick = 64;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            sapi.Event.Timer(OnTick, TickIntervalSeconds);
        }

        private void OnTick()
        {
            IWorldChunk[] loadedChunks = sapi.WorldManager.AllLoadedChunks;
            if (loadedChunks == null || loadedChunks.Length == 0) return;

            Random rng = sapi.World.Rand;
            int chunkSize = sapi.WorldManager.ChunkSize; // typically 32

            for (int i = 0; i < SamplesPerTick; i++)
            {
                // Pick a random loaded chunk
                IWorldChunk chunk = loadedChunks[rng.Next(loadedChunks.Length)];
                if (chunk == null) continue;

                // Get the chunk's block-coordinate origin
                long index = sapi.WorldManager.ChunkIndex(chunk);
                sapi.WorldManager.ChunkCoordFromIndex(index, out int cx, out int cy, out int cz);

                int originX = cx * chunkSize;
                int originZ = cz * chunkSize;

                // Pick a random (x, z) within this chunk
                int x = originX + rng.Next(chunkSize);
                int z = originZ + rng.Next(chunkSize);

                // Find the surface block at this (x, z)
                int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));
                BlockPos surfacePos = new BlockPos(x, y, z, 0);

                Block surfaceBlock = sapi.World.BlockAccessor.GetBlock(surfacePos);
                if (!IsForestFloor(surfaceBlock)) continue;

                // The block above must be air
                BlockPos abovePos = surfacePos.UpCopy();
                Block aboveBlock = sapi.World.BlockAccessor.GetBlock(abovePos);
                if (aboveBlock == null || aboveBlock.BlockId != 0) continue;

                // Probability check
                if (rng.NextDouble() > SpawnChance) continue;

                // Check for nearby trees/saplings
                if (HasNearbyTreeOrSapling(surfacePos)) continue;

                // Pick a climate-appropriate sapling
                Block? saplingBlock = ChooseSapling(surfacePos);
                if (saplingBlock == null) continue;

                // Place it
                sapi.World.BlockAccessor.SetBlock(saplingBlock.BlockId, abovePos);
                sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(abovePos);
                Mod.Logger.Notification($"[ForestRegrowth] Spawned {saplingBlock.Code.Path} at ({abovePos.X}, {abovePos.Y}, {abovePos.Z})");
            }
        }

        private bool IsForestFloor(Block block)
        {
            return block?.Code?.Domain == "game" &&
                   block.Code.Path.StartsWith("forestfloor");
        }

        private bool HasNearbyTreeOrSapling(BlockPos centerPos)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos checkPos = new BlockPos(0);

            for (int dx = -ClearRadius; dx <= ClearRadius; dx++)
            {
                for (int dz = -ClearRadius; dz <= ClearRadius; dz++)
                {
                    if (dx * dx + dz * dz > ClearRadius * ClearRadius) continue;

                    for (int dy = -2; dy <= 24; dy++)
                    {
                        checkPos.Set(centerPos.X + dx, centerPos.Y + dy, centerPos.Z + dz);
                        Block b = ba.GetBlock(checkPos);
                        if (b == null || b.BlockId == 0) continue;

                        string path = b.Code?.Path ?? "";

                        if (path.StartsWith("log-") || path.StartsWith("aged-log-"))
                            return true;

                        if (path.StartsWith("sapling-"))
                            return true;
                    }
                }
            }
            return false;
        }

        private Block? ChooseSapling(BlockPos pos)
        {
            ClimateCondition? climate = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
            if (climate == null) return null;

            var candidates = new List<string>();
            float temp = climate.Temperature;
            float rain = climate.Rainfall;

            if (temp < 8f)
            {
                candidates.Add("pine");
                candidates.Add("larch");
                if (rain > 0.45f) candidates.Add("birch");
                if (temp > 2f) candidates.Add("birch");
            }

            if (temp >= 6f && temp < 18f)
            {
                candidates.Add("oak");
                if (rain > 0.5f) candidates.Add("maple");
                if (rain > 0.55f) candidates.Add("hornbeam");
                candidates.Add("birch");
                if (temp > 10f) candidates.Add("walnut");
            }

            if (temp >= 16f)
            {
                candidates.Add("acacia");
                if (rain > 0.55f) candidates.Add("kapok");
                if (rain > 0.6f) candidates.Add("baldcypress");
                candidates.Add("oak");
            }

            candidates.Add("oak");

            Shuffle(candidates, sapi.World.Rand);
            foreach (string tree in candidates)
            {
                Block b = sapi.World.GetBlock(new AssetLocation("game", $"sapling-{tree}-free"));
                if (b != null && b.BlockId != 0) return b;

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
