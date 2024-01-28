using System;
using System.Collections.Generic;
using global::System.Drawing;
using global::System.IO;
using System.Linq;
using System.Threading.Tasks;
using global::Newtonsoft.Json.Linq;
using global::SM64Lib.Model.Conversion.Fast3DWriting;
using SM64Lib.Model.Conversion;

namespace SM64Lib.Model.Fast3D
{
    public class TextureFormatSettings
    {
        public List<Entry> Entries { get; private set; } = new List<Entry>();
        public List<DisplaylistProps> CustomDisplayLists { get; private set; } = new List<DisplaylistProps>();

        public async Task Load(string fileName)
        {
            if (File.Exists(fileName))
            {
                bool success = false;
                var streamReader = new StreamReader(fileName);
                string content = await streamReader.ReadToEndAsync();
                streamReader.Close();
                Entries.Clear();
                CustomDisplayLists.Clear();
                try
                {
                    var settings = JObject.Parse(content).ToObject<TextureFormatSettings>();
                    Entries.AddRange(settings.Entries);
                    CustomDisplayLists.AddRange(settings.CustomDisplayLists);
                    success = true;
                }
                catch (Exception)
                {
                }

                if (!success)
                {
                    try
                    {
                        Entries.AddRange(JArray.Parse(content).ToObject<Entry[]>());
                        success = true;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        public async Task Save(string fileName)
        {
            var sw = new StreamWriter(fileName);
            await sw.WriteAsync(JObject.FromObject(this).ToString());
            sw.Flush();
            sw.Close();
        }

        public Entry GetEntry(string matName)
        {
            foreach (Entry e in Entries)
            {
                if ((e.MaterialName ?? "") == (matName ?? ""))
                {
                    return e;
                }
            }

            var ne = new Entry();
            ne.MaterialName = matName;
            Entries.Add(ne);
            return ne;
        }

        public class Entry
        {
            public bool Include { get; set; } = true;
            public string MaterialName { get; set; } = "";
            public string TextureFormat { get; set; } = "";
            public bool IsScrollingTexture { get; set; } = false;
            public DisplaylistSelectionSettings DisplaylistSelection { get; set; } = new DisplaylistSelectionSettings();
            public FaceCullingMode FaceCullingMode { get; set; } = FaceCullingMode.Back;
            public bool EnableMirrorS { get; set; } = false;
            public bool EnableMirrorT { get; set; } = false;
            public bool EnableClampS { get; set; } = false;
            public bool EnableClampT { get; set; } = false;
            public bool EnableCrystalEffect { get; set; } = false;
            public float? TransparencyLimit { get; set; } = null;
            public RotateFlipType RotateFlip { get; set; } = RotateFlipType.RotateNoneFlipNone;
            public TextureFilter TextureFilter { get; set; } = TextureFilter.Bilerp;
        }
    }
}