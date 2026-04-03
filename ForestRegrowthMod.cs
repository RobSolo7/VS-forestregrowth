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
        // CS8618: initialised in StartServerSide before any other method runs
        private ICoreServerAPI sapi = null!;

        private const int TickIntervalMs = 60_000;
        private const int ClearRadius = 15;
        private const double SpawnChance = 0.05;
        private const int SamplesPerPlayerPerTick = 8;
        private const int ScanRange = 48;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            sapi.Event.Timer(OnTick, TickIntervalMs / 1000.0);
        }

        private void OnTick()
        {
            // CS8600: use pattern matching instead of direct cast
            if (sapi.World.AllOnlinePlayers is not IServerPlayer[] players || players.Length == 0) return;

            Random rng = sapi.World.Rand;

            foreach (IServerPlayer player in players)
            {
                if (player.ConnectionState != EnumClientState.Playing) continue;

                // CS8600: explicit nullable type
                BlockPos? playerPos = player.Entity?.Pos?.AsBlockPos;
                if (playerPos == null) continue;

                for (int i = 0; i < SamplesPerPlayerPerTick; i++)
                {
                    int dx = rng.Next(-ScanRange, ScanRange + 1);
                    int dz = rng.Next(-ScanRange, ScanRange + 1);

                    int x = playerPos.X + dx;
                    int z = playerPos.Z + dz;

                    // CS0618: use dimensionId overload to avoid obsolete constructor
                    int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));

                    BlockPos surfacePos = new BlockPos(x, y, z, 0);
                    Block surfaceBlock = sapi.World.BlockAccessor.GetBlock(surfacePos);

                    if (!IsForestFloor(surfaceBlock)) continue;

                    BlockPos abovePos = surfacePos.UpCopy();
                    Block aboveBlock = sapi.World.BlockAccessor.GetBlock(abovePos);
                    if (aboveBlock == null || aboveBlock.BlockId != 0) continue;

                    if (rng.NextDouble() > SpawnChance) continue;

                    if (HasNearbyTreeOrSapling(surfacePos)) continue;

                    // CS8603: nullable return type
                    Block? saplingBlock = ChooseSapling(surfacePos);
                    if (saplingBlock == null) continue;

                    sapi.World.BlockAccessor.SetBlock(saplingBlock.BlockId, abovePos);
                    sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(abovePos);
                    Mod.Logger.Notification($"[ForestRegrowth] Spawned {saplingBlock.Code.Path} at ({abovePos.X}, {abovePos.Y}, {abovePos.Z})");
                }
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

            // CS0618: use dimensionId overload
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
            // CS8603: nullable return type
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
