using System.Collections.Generic;
using System.IO;

namespace SM64Lib
{
    public class FilePathsConfiguration
    {

        // S T A T I C   M E M B E R S

        public static FilePathsConfiguration DefaultConfiguration { get; private set; } = new FilePathsConfiguration();

        public static string[] AllFileKeys
        {
            get
            {
                return new[] { "rn64crc.exe","ApplyPPF3.exe", "Level Tabel.json", "Update-Patches.json", "Update Patches Folder", "Text Profiles.json", "BaseTweak", "sm64extend.exe", "Original Level Pointers.bin", "armips.exe", "Flips.exe", "nconvert.exe" };
            }
        }

        // F I E L D S

        private readonly Dictionary<string, string> dic = new Dictionary<string, string>();

        // P R O P E R T I E S

        public IReadOnlyDictionary<string, string> Files
        {
            get
            {
                return dic;
            }
        }

        // C O N S T R U C T O R

        public FilePathsConfiguration()
        {
            foreach (string key in AllFileKeys)
                dic.Add(key, string.Empty);
            
            RecursiveFindFiles("./");
        }

        void RecursiveFindFiles(string path) {
            foreach (string file in Directory.GetFiles(path)) {
                string fname = Path.GetFileName(file);
                if (dic.ContainsKey(fname)) {
                    dic[fname] = file;
                }
            }

            foreach (string dir in Directory.GetDirectories(path)) {
                RecursiveFindFiles(dir);
            }
        }

        // M E T H O D S

        public void SetFilePath(string key, string path)
        {
            dic[key] = path;
        }
    }
}