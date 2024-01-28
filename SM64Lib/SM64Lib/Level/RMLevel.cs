
using Newtonsoft.Json;
using SM64Lib.Configuration;

namespace SM64Lib.Levels
{
    public class RMLevel : Level
    {
        [JsonProperty]
        public LevelConfig Config { get; private set; }

        [JsonIgnore]
        public string Name
        {
            get => Config.LevelName;
            set => Config.LevelName = value;
        }

        public RMLevel(ushort LevelID, int LevelIndex, RomManager rommgr) : base(LevelID, LevelIndex, rommgr)
        {
            Config = new LevelConfig();
        }

        public RMLevel(LevelConfig config, RomManager rommgr) : base(rommgr)
        {
            Config = config;
        }

        [JsonConstructor]
        private RMLevel(JsonConstructorAttribute attr) : base(attr)
        {
        }
    }
}