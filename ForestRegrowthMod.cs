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

        private const double TickIntervalSeconds = 10.0;
        private const int ClearRadius = 15;
        private const double SpawnChance = 1.0;
        private const int SamplesPerTick = 64;
        private const int ScanRange = 80;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            sapi.Event.Timer(OnTick, TickIntervalSeconds);

            sapi.ChatCommands.Create("frdebug")
                .WithDescription("Forest Regrowth: prints block codes around your feet")
                .HandleWith(OnDebugCommand);

            Mod.Logger.Notification("[ForestRegrowth] Mod loaded. Type /frdebug in game to inspect blocks.");
        }

        private TextCommandResult OnDebugCommand(TextCommandCallingArgs args)
        {
            IServerPlayer player = (IServerPlayer)args.Caller.Player;
            BlockPos pos = player.Entity.Pos.AsBlockPos;

            Mod.Logger.Notification($"[ForestRegrowth] Player is at ({pos.X},{pos.Y},{pos.Z})");

            // Check several Y levels at the player's X,Z to find what's there
            for (int dy = -3; dy <= 1; dy++)
            {
                BlockPos checkPos = new BlockPos(pos.X, pos.Y + dy, pos.Z, 0);
                Block b = sapi.World.BlockAccessor.GetBlock(checkPos);
                bool isFF = b.Code?.Path.Contains("forestfloor") == true;
                Mod.Logger.Notification($"[ForestRegrowth] Y+{dy} ({checkPos.Y}): {b?.Code?.Domain}:{b?.Code?.Path} | IsForestFloor={isFF}");
            }

            // Check GetTerrainMapheightAt
            int mapHeight = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(pos.X, 0, pos.Z, 0));
            Mod.Logger.Notification($"[ForestRegrowth] GetTerrainMapheightAt = {mapHeight}");
            Block mapBlock = sapi.World.BlockAccessor.GetBlock(new BlockPos(pos.X, mapHeight, pos.Z, 0));
            bool isMapFF = mapBlock.Code?.Path.Contains("forestfloor") == true;
            Mod.Logger.Notification($"[ForestRegrowth] Block at mapHeight: {mapBlock?.Code?.Domain}:{mapBlock?.Code?.Path} | IsForestFloor={isMapFF}");

            return TextCommandResult.Success("Check server-main.log for results.");
        }

        private void OnTick()
        {
            if (sapi.World.AllOnlinePlayers is not IServerPlayer[] players || players.Length == 0) return;

            Random rng = sapi.World.Rand;

            int forestFloorFound = 0;
            int aboveNotAir = 0;
            int nearbyTreeBlocked = 0;
            int saplingPlaced = 0;
            int noSaplingType = 0;

            foreach (IServerPlayer player in players)
            {
                if (player.ConnectionState != EnumClientState.Playing) continue;

                BlockPos? playerPos = player.Entity?.Pos?.AsBlockPos;
                if (playerPos == null) continue;

                for (int i = 0; i < SamplesPerTick; i++)
                {
                    int dx = rng.Next(-ScanRange, ScanRange + 1);
                    int dz = rng.Next(-ScanRange, ScanRange + 1);

                    int x = playerPos.X + dx;
                    int z = playerPos.Z + dz;

                    int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));
                    BlockPos surfacePos = new BlockPos(x, y, z, 0);

                    Block surfaceBlock = sapi.World.BlockAccessor.GetBlock(surfacePos);
                    if (!(surfaceBlock.Code?.Path.Contains("forestfloor") ?? false)) continue;
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
            }

            Mod.Logger.Notification($"[ForestRegrowth] Results: forestFloor={forestFloorFound} aboveNotAir={aboveNotAir} nearbyTreeBlocked={nearbyTreeBlocked} noSaplingType={noSaplingType} placed={saplingPlaced}");
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

            if (candidates.Count == 0)
{
    candidates.Add("oak");
}

            // Remove duplicates to avoid bias (e.g. oak appearing multiple times)
var unique = new HashSet<string>(candidates);

var valid = new List<Block>();

foreach (string tree in unique)
{
    Block b = sapi.World.GetBlock(new AssetLocation("game", $"sapling-{tree}-free"));
    if (b != null && b.BlockId != 0)
    {
        valid.Add(b);
        continue;
    }

    b = sapi.World.GetBlock(new AssetLocation("game", $"sapling-{tree}"));
    if (b != null && b.BlockId != 0)
    {
        valid.Add(b);
    }
}

// If nothing valid found, fail
if (valid.Count == 0) return null;

// Pick a random valid sapling
return valid[sapi.World.Rand.Next(valid.Count)];
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
