using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ForestRegrowth
{
    public class ForestRegrowthMod : ModSystem
    {
        private ICoreServerAPI sapi = null!;

        private const double TickIntervalSeconds = 10.0;  // fast for debugging
        private const int ClearRadius = 15;
        private const double SpawnChance = 1.0;           // 100% for debugging
        private const int SamplesPerTick = 64;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            sapi.Event.Timer(OnTick, TickIntervalSeconds);
            Mod.Logger.Notification("[ForestRegrowth] Mod loaded and timer registered.");
        }

        private void OnTick()
        {
            Mod.Logger.Notification("[ForestRegrowth] Tick fired.");

            Dictionary<long, IServerChunk> loadedChunks = sapi.WorldManager.AllLoadedChunks;
            if (loadedChunks == null || loadedChunks.Count == 0)
            {
                Mod.Logger.Notification("[ForestRegrowth] No loaded chunks found.");
                return;
            }

            Mod.Logger.Notification($"[ForestRegrowth] {loadedChunks.Count} chunks loaded.");

            Random rng = sapi.World.Rand;
            int chunkSize = sapi.WorldManager.ChunkSize;

            long[] chunkKeys = new long[loadedChunks.Count];
            loadedChunks.Keys.CopyTo(chunkKeys, 0);

            int forestFloorFound = 0;
            int aboveNotAir = 0;
            int nearbyTreeBlocked = 0;
            int saplingPlaced = 0;
            int noSaplingType = 0;

            for (int i = 0; i < SamplesPerTick; i++)
            {
                long chunkIndex = chunkKeys[rng.Next(chunkKeys.Length)];

                int mapChunkSizeY = sapi.WorldManager.MapSizeY / chunkSize;
                int mapChunkSizeZ = sapi.WorldManager.MapSizeZ / chunkSize;

                int cy = (int)(chunkIndex % mapChunkSizeY);
                int cz = (int)(chunkIndex / mapChunkSizeY % mapChunkSizeZ);
                int cx = (int)(chunkIndex / mapChunkSizeY / mapChunkSizeZ);

                int originX = cx * chunkSize;
                int originZ = cz * chunkSize;

                int x = originX + rng.Next(chunkSize);
                int z = originZ + rng.Next(chunkSize);

                int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));
                BlockPos surfacePos = new BlockPos(x, y, z, 0);

                Block surfaceBlock = sapi.World.BlockAccessor.GetBlock(surfacePos);
                if (!IsForestFloor(surfaceBlock)) continue;

                forestFloorFound++;

                BlockPos abovePos = surfacePos.UpCopy();
                Block aboveBlock = sapi.World.BlockAccessor.GetBlock(abovePos);
                if (aboveBlock == null || aboveBlock.BlockId != 0)
                {
                    aboveNotAir++;
                    continue;
                }

                if (HasNearbyTreeOrSapling(surfacePos))
                {
                    nearbyTreeBlocked++;
                    continue;
                }

                Block? saplingBlock = ChooseSapling(surfacePos);
                if (saplingBlock == null)
                {
                    noSaplingType++;
                    continue;
                }

                sapi.World.BlockAccessor.SetBlock(saplingBlock.BlockId, abovePos);
                sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(abovePos);
                saplingPlaced++;
                Mod.Logger.Notification($"[ForestRegrowth] Spawned {saplingBlock.Code.Path} at ({abovePos.X}, {abovePos.Y}, {abovePos.Z})");
            }

            Mod.Logger.Notification($"[ForestRegrowth] Results: forestFloor={forestFloorFound} aboveNotAir={aboveNotAir} nearbyTreeBlocked={nearbyTreeBlocked} noSaplingType={noSaplingType} placed={saplingPlaced}");
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
