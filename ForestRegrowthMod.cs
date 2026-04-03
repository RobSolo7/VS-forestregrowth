using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ForestRegrowth
{
    // 1. Create a Config Class to hold adjustable values
    public class ForestRegrowthConfig
    {
        public int ClearRadius { get; set; } = 15;
        public int SamplesPerTick { get; set; } = 64;
        public double TickIntervalSeconds { get; set; } = 10.0;
        // Adding a toggle just in case admins want to pause regrowth
        public bool Enabled { get; set; } = true; 
    }

    public class ForestRegrowthMod : ModSystem
    {
        private ICoreServerAPI sapi = null!;
        private ForestRegrowthConfig config = new ForestRegrowthConfig();
        
        // Caching block IDs for O(1) instantaneous lookups
        private HashSet<int> treeAndSaplingIds = new HashSet<int>();
        private bool idsCached = false;
        
        private long tickListenerId;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            LoadConfig();

            tickListenerId = sapi.Event.Timer(OnTick, config.TickIntervalSeconds);

            // Register Commands
            RegisterCommands();

            Mod.Logger.Notification("[ForestRegrowth] Mod loaded.");
        }

        private void LoadConfig()
        {
            try 
            {
                var loadedConfig = sapi.LoadModConfig<ForestRegrowthConfig>("forestregrowth.json");
                if (loadedConfig != null)
                {
                    config = loadedConfig;
                }
                else
                {
                    sapi.StoreModConfig(config, "forestregrowth.json");
                }
            }
            catch (Exception)
            {
                Mod.Logger.Warning("[ForestRegrowth] Failed to load config, using defaults.");
                config = new ForestRegrowthConfig();
            }
        }

        private void RegisterCommands()
        {
            var cmd = sapi.ChatCommands.Create("fr")
                .WithDescription("Forest Regrowth Master Command")
                .RequiresPrivilege(Privilege.controlserver);

            cmd.BeginSubCommand("debug")
                .WithDescription("Prints block codes around your feet")
                .HandleWith(OnDebugCommand)
                .EndSubCommand();

            cmd.BeginSubCommand("config")
                .WithDescription("Change configuration on the fly. Usage: /fr config [setting] [value]")
                .WithArgs(sapi.ChatCommands.Parsers.Word("setting"), sapi.ChatCommands.Parsers.Double("value"))
                .HandleWith(OnConfigCommand)
                .EndSubCommand();
        }

        private TextCommandResult OnConfigCommand(TextCommandCallingArgs args)
        {
            string setting = (string)args[0];
            double value = (double)args[1];

            switch (setting.ToLower())
            {
                case "clearradius":
                    config.ClearRadius = (int)value;
                    break;
                case "samplespertick":
                    config.SamplesPerTick = (int)value;
                    break;
                case "tickinterval":
                    config.TickIntervalSeconds = value;
                    sapi.Event.UnregisterCallback(tickListenerId);
                    tickListenerId = sapi.Event.Timer(OnTick, config.TickIntervalSeconds);
                    break;
                case "enabled":
                    config.Enabled = value > 0;
                    break;
                default:
                    return TextCommandResult.Error("Unknown setting. Valid: clearradius, samplespertick, tickinterval, enabled");
            }

            sapi.StoreModConfig(config, "forestregrowth.json");
            return TextCommandResult.Success($"[ForestRegrowth] Set {setting} to {value}");
        }

        private void CacheTreeIds()
        {
            // We run this once during the first tick to ensure all mods have registered their blocks
            foreach (Block block in sapi.World.Blocks)
            {
                if (block?.Code == null) continue;
                string path = block.Code.Path;
                if (path.StartsWith("log-") || path.StartsWith("aged-log-") || path.StartsWith("sapling-"))
                {
                    treeAndSaplingIds.Add(block.BlockId);
                }
            }
            idsCached = true;
            Mod.Logger.Notification($"[ForestRegrowth] Cached {treeAndSaplingIds.Count} tree/sapling Block IDs for fast lookup.");
        }

        private void OnTick()
        {
            if (!config.Enabled) return;
            if (!idsCached) CacheTreeIds();

            if (sapi.World.AllOnlinePlayers is not IServerPlayer[] players || players.Length == 0) return;

            Random rng = sapi.World.Rand;
            
            // Match the server's render distance. (ViewDistance is in chunks. 1 chunk = 32 blocks)
            int scanRange = sapi.Server.Config.ViewDistance * 32;

            foreach (IServerPlayer player in players)
            {
                if (player.ConnectionState != EnumClientState.Playing) continue;

                BlockPos? playerPos = player.Entity?.Pos?.AsBlockPos;
                if (playerPos == null) continue;

                for (int i = 0; i < config.SamplesPerTick; i++)
                {
                    int dx = rng.Next(-scanRange, scanRange + 1);
                    int dz = rng.Next(-scanRange, scanRange + 1);

                    int x = playerPos.X + dx;
                    int z = playerPos.Z + dz;

                    int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));
                    BlockPos surfacePos = new BlockPos(x, y, z, 0);

                    Block surfaceBlock = sapi.World.BlockAccessor.GetBlock(surfacePos);
                    if (!(surfaceBlock.Code?.Path.Contains("forestfloor") ?? false)) continue;

                    BlockPos abovePos = surfacePos.UpCopy();
                    
                    // Fast integer check to ensure air
                    Block aboveBlock = sapi.World.BlockAccessor.GetBlock(abovePos);
                    if (aboveBlock.BlockId != 0) continue;

                    if (HasNearbyTreeOrSapling(surfacePos)) continue;

                    Block? saplingBlock = ChooseSapling(surfacePos);
                    if (saplingBlock == null) continue;

                    sapi.World.BlockAccessor.SetBlock(saplingBlock.BlockId, abovePos);
                    sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(abovePos);
                }
            }
        }

        private bool HasNearbyTreeOrSapling(BlockPos centerPos)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos checkPos = new BlockPos(0);
            
            int radius = config.ClearRadius;
            int radiusSq = radius * radius;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    // Circular check is faster than square
                    if (dx * dx + dz * dz > radiusSq) continue;

                    // Optimization: We don't need to check all the way to Y=24 immediately.
                    // Start from the ground up. If there's a tree, we'll hit the trunk right away.
                    for (int dy = -1; dy <= 5; dy++) 
                    {
                        checkPos.Set(centerPos.X + dx, centerPos.Y + dy, centerPos.Z + dz);
                        
                        // GetBlockId is vastly more performant than GetBlock, saving RAM allocation and CPU cycles
                        int blockId = ba.GetBlockId(checkPos);
                        if (blockId == 0) continue; 

                        // O(1) lookup against our cached list
                        if (treeAndSaplingIds.Contains(blockId))
                        {
                            return true;
                        }
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

            // ... (Keep your existing ChooseSapling logic here, it is perfectly fine) ...
            
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

            if (candidates.Count == 0) candidates.Add("oak");

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
                if (b != null && b.BlockId != 0) valid.Add(b);
            }

            if (valid.Count == 0) return null;
            return valid[sapi.World.Rand.Next(valid.Count)];
        }

        private TextCommandResult OnDebugCommand(TextCommandCallingArgs args)
        {
             // ... (Keep your existing debug logic here) ...
             return TextCommandResult.Success("Check server-main.log for results.");
        }
    }
}
