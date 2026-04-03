using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ForestRegrowth
{
    public class ForestRegrowthConfig
    {
        public int ClearRadius { get; set; } = 15;
        public int SamplesPerTick { get; set; } = 64;
        public double TickIntervalSeconds { get; set; } = 10.0;
        public int ScanRange { get; set; } = 80;
        public bool Enabled { get; set; } = true;
        public bool Verbose { get; set; } = false; 
    }

    public class ForestRegrowthMod : ModSystem
    {
        private ICoreServerAPI sapi = null!;
        private ForestRegrowthConfig config = new ForestRegrowthConfig();
        private HashSet<int> treeAndSaplingIds = new HashSet<int>();
        private bool idsCached = false;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            LoadConfig();

            // Set the timer based on config
            sapi.Event.Timer(OnTick, config.TickIntervalSeconds);

            RegisterCommands();

            Mod.Logger.Notification("[ForestRegrowth] Mod loaded. Type /forest for commands.");
        }

        private void LoadConfig()
        {
            try 
            {
                var loadedConfig = sapi.LoadModConfig<ForestRegrowthConfig>("forestregrowth.json");
                if (loadedConfig != null) config = loadedConfig;
                else sapi.StoreModConfig(config, "forestregrowth.json");
            }
            catch (Exception)
            {
                config = new ForestRegrowthConfig();
            }
        }

        private void RegisterCommands()
        {
            // Root command /forest
            var cmd = sapi.ChatCommands.Create("forest")
                .WithDescription("Forest Regrowth main command")
                .RequiresPrivilege(Privilege.controlserver);

            // /forest debug (Toggle)
            cmd.BeginSubCommand("debug")
                .WithDescription("Toggles live planting notifications in the server console")
                .HandleWith(OnToggleDebug)
                .EndSubCommand();

            // /forest config
            cmd.BeginSubCommand("config")
                .WithDescription("Change settings. Usage: /forest config [setting] [value]")
                .WithArgs(sapi.ChatCommands.Parsers.Word("setting"), sapi.ChatCommands.Parsers.Double("value"))
                .HandleWith(OnConfigCommand)
                .EndSubCommand();

            // /forest inspect (The old block-printing debug)
            cmd.BeginSubCommand("inspect")
                .WithDescription("Prints technical block info at your current position to console")
                .HandleWith(OnInspectCommand)
                .EndSubCommand();
        }

        private TextCommandResult OnToggleDebug(TextCommandCallingArgs args)
        {
            config.Verbose = !config.Verbose;
            sapi.StoreModConfig(config, "forestregrowth.json");
            string status = config.Verbose ? "ENABLED" : "DISABLED";
            return TextCommandResult.Success($"[ForestRegrowth] Verbose debug logging is now {status}");
        }

        private TextCommandResult OnConfigCommand(TextCommandCallingArgs args)
        {
            string setting = (string)args[0];
            double value = (double)args[1];

            switch (setting.ToLower())
            {
                case "clearradius": config.ClearRadius = (int)value; break;
                case "samplespertick": config.SamplesPerTick = (int)value; break;
                case "scanrange": config.ScanRange = (int)value; break;
                case "enabled": config.Enabled = value > 0; break;
                case "tickinterval":
                    config.TickIntervalSeconds = value;
                    sapi.StoreModConfig(config, "forestregrowth.json");
                    return TextCommandResult.Success($"[ForestRegrowth] Tick interval set to {value}s. A server restart is required to change the timer frequency.");
                default:
                    return TextCommandResult.Error("Valid settings: clearradius, samplespertick, scanrange, enabled, tickinterval");
            }

            sapi.StoreModConfig(config, "forestregrowth.json");
            return TextCommandResult.Success($"[ForestRegrowth] {setting} set to {value}");
        }

        private void CacheTreeIds()
        {
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
        }

        private void OnTick()
        {
            if (!config.Enabled) return;
            if (!idsCached) CacheTreeIds();

            var players = sapi.World.AllOnlinePlayers;
            if (players == null || players.Length == 0) return;

            Random rng = sapi.World.Rand;

            foreach (IServerPlayer player in players)
            {
                if (player.ConnectionState != EnumClientState.Playing || player.Entity == null) continue;

                BlockPos playerPos = player.Entity.Pos.AsBlockPos;

                for (int i = 0; i < config.SamplesPerTick; i++)
                {
                    int dx = rng.Next(-config.ScanRange, config.ScanRange + 1);
                    int dz = rng.Next(-config.ScanRange, config.ScanRange + 1);

                    int x = playerPos.X + dx;
                    int z = playerPos.Z + dz;

                    int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));
                    BlockPos surfacePos = new BlockPos(x, y, z, 0);

                    Block surfaceBlock = sapi.World.BlockAccessor.GetBlock(surfacePos);
                    if (!(surfaceBlock.Code?.Path.Contains("forestfloor") ?? false)) continue;

                    BlockPos abovePos = surfacePos.UpCopy();
                    if (sapi.World.BlockAccessor.GetBlockId(abovePos) != 0) continue;

                    if (HasNearbyTreeOrSapling(surfacePos)) continue;

                    Block? saplingBlock = ChooseSapling(surfacePos);
                    if (saplingBlock == null) continue;

                    sapi.World.BlockAccessor.SetBlock(saplingBlock.BlockId, abovePos);
                    sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(abovePos);

                    if (config.Verbose)
                    {
                        sapi.Logger.Notification($"[ForestRegrowth] Planted {saplingBlock.Code} at {abovePos}");
                    }
                }
            }
        }

        private bool HasNearbyTreeOrSapling(BlockPos centerPos)
        {
            IBlockAccessor ba = sapi.World.BlockAccessor;
            BlockPos checkPos = new BlockPos(0);
            int radiusSq = config.ClearRadius * config.ClearRadius;

            for (int dx = -config.ClearRadius; dx <= config.ClearRadius; dx++)
            {
                for (int dz = -config.ClearRadius; dz <= config.ClearRadius; dz++)
                {
                    if (dx * dx + dz * dz > radiusSq) continue;

                    for (int dy = -1; dy <= 5; dy++) 
                    {
                        checkPos.Set(centerPos.X + dx, centerPos.Y + dy, centerPos.Z + dz);
                        int blockId = ba.GetBlockId(checkPos);
                        if (blockId != 0 && treeAndSaplingIds.Contains(blockId)) return true;
                    }
                }
            }
            return false;
        }

        private Block? ChooseSapling(BlockPos pos)
        {
            ClimateCondition climate = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
            if (climate == null) return null;

            List<string> candidates = new List<string>();
            float temp = climate.Temperature;
            float rain = climate.Rainfall;

            if (temp < 8f) { candidates.Add("pine"); candidates.Add("larch"); if (rain > 0.45f || temp > 2f) candidates.Add("birch"); }
            if (temp >= 6f && temp < 18f) { candidates.Add("oak"); if (rain > 0.5f) candidates.Add("maple"); if (rain > 0.55f) candidates.Add("hornbeam"); candidates.Add("birch"); if (temp > 10f) candidates.Add("walnut"); }
            if (temp >= 16f) { candidates.Add("acacia"); if (rain > 0.55f) candidates.Add("kapok"); if (rain > 0.6f) candidates.Add("baldcypress"); candidates.Add("oak"); }

            if (candidates.Count == 0) candidates.Add("oak");

            var unique = new HashSet<string>(candidates);
            List<Block> valid = new List<Block>();

            foreach (string tree in unique)
            {
                Block b = sapi.World.GetBlock(new AssetLocation("game", $"sapling-{tree}-free")) ?? sapi.World.GetBlock(new AssetLocation("game", $"sapling-{tree}"));
                if (b != null && b.BlockId != 0) valid.Add(b);
            }

            return valid.Count > 0 ? valid[sapi.World.Rand.Next(valid.Count)] : null;
        }

        private TextCommandResult OnInspectCommand(TextCommandCallingArgs args)
        {
            IServerPlayer player = (IServerPlayer)args.Caller.Player;
            BlockPos pos = player.Entity.Pos.AsBlockPos;
            sapi.Logger.Notification($"[ForestRegrowth] Inspecting ({pos.X},{pos.Y},{pos.Z})");

            for (int dy = -2; dy <= 1; dy++)
            {
                Block b = sapi.World.BlockAccessor.GetBlock(pos.X, pos.Y + dy, pos.Z);
                sapi.Logger.Notification($"  Y{dy}: {b.Code} (ForestFloor: {b.Code?.Path.Contains("forestfloor")})");
            }
            return TextCommandResult.Success("Inspection complete. Check server-main.log");
        }
    }
}
