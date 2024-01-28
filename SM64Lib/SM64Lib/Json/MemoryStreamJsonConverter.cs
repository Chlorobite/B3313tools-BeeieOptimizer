using Newtonsoft.Json;
using SM64Lib.Behaviors.Script;
using SM64Lib.Geolayout.Script;
using SM64Lib.Levels.Script;
using SM64Lib.Model.Fast3D;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SM64Lib.Json
{
    internal class MemoryStreamJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(MemoryStream).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var msData = serializer.Deserialize<byte[]>(reader);
            var ms = new MemoryStream();
            ms.Write(msData, 0, msData.Length);
            ms.Position = 0;
            return ms;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var ms = (MemoryStream)value;
            serializer.Serialize(writer, ms.ToArray());
        }
    }
}
