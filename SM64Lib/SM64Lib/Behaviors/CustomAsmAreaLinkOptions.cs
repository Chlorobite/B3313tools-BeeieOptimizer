using Newtonsoft.Json;
using SM64Lib.ASM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SM64Lib.Behaviors
{
    public class CustomAsmAreaLinkOptions
    {
        // Temporary fix - Remove custom property code and make it auto property in v1.?
        private CustomAsmAreaConfig customAsmAreaConfig;
        public CustomAsmAreaConfig CustomAsmAreaConfig
        {
            get => customAsmAreaConfig ?? CustomAsm?.Config;
            set => customAsmAreaConfig = value;
        }
        // Temporary fix for old files - Remove property in v1.?
        [JsonProperty]
        public CustomAsmArea CustomAsm { get; private set; }
        public bool Loop { get; set; }
    }
}
