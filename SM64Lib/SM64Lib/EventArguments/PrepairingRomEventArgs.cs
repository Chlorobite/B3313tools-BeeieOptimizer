using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SM64Lib.EventArguments
{
    public class PrepairingRomEventArgs : EventArgs
    {
        public IEnumerable<BaseTweakScriptInfo> ScriptInfos { get; set; }
    }
}
