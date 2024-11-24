using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using SM64Lib;
using SM64Lib.Geolayout;
using SM64Lib.Levels;
using SM64Lib.Levels.Script;
using SM64Lib.Levels.ScrolTex;
using SM64Lib.Model.Collision;
using SM64Lib.Model.Fast3D;
using Z.Core.Extensions;

using static Gbi;
using static ReadWrite;


void RunProcess(string process) {
    string name = process.Split(' ')[0];
    string args = process[(name.Length + 1)..];

    Process? p = Process.Start(new ProcessStartInfo(name, args) {
        CreateNoWindow = true,
        UseShellExecute = false,
    });

    if (p == null) {
        Console.WriteLine($"Failed to run {process}");
        return;
    }
    p.WaitForExit();
    p.Close();
}

bool IsTriDegenerate(Vtx_tn v1, Vtx_tn v2, Vtx_tn v3) {
    long ax = v2.x - v1.x, ay = v2.y - v1.y, az = v2.z - v1.z;
    long bx = v3.x - v1.x, by = v3.y - v1.y, bz = v3.z - v1.z;

    // Cross product
    long cx = ay * bz - az * by;
    long cy = az * bx - ax * bz;
    long cz = ax * by - ay * bx;

    // Degenerate if the cross product is zero
    return cx == 0 && cy == 0 && cz == 0;
}

double TriArea(Vtx_tn v1, Vtx_tn v2, Vtx_tn v3) {
    // Edge vectors
    long ax = v2.x - v1.x, ay = v2.y - v1.y, az = v2.z - v1.z;
    long bx = v3.x - v1.x, by = v3.y - v1.y, bz = v3.z - v1.z;

    // Cross product
    long cx = ay * bz - az * by;
    long cy = az * bx - ax * bz;
    long cz = ax * by - ay * bx;

    // Magnitude of the cross product
    double crossMagnitude = Math.Sqrt(cx * cx + cy * cy + cz * cz);

    // Area of the triangle
    return crossMagnitude / 2.0;
}

double Lerp(double a, double b, double t) {
    return a * (1.0 - t) + b * t;
}
double InverseLerp(double a, double b, double value) {
    return (value - a) / (b - a);
}

double Remap(double value, double oldMin, double oldMax, double newMin, double newMax) {
    double t = InverseLerp(oldMin, oldMax, value);
    return Lerp(newMin, newMax, t);
}

double uvmod(double a, double b) {
    return (a + 65536) % b;
}



void PrintRomSize(RomManager manger, string context) {
    RomSpaceInfo info = manger.GetRomSpaceInfo();
    Console.WriteLine($"ROM size {context}: {((0x1210000 + info.TotalUsedSpace) / 1024) / 1024.0} MiB");
}

void SaveAndPrintRomSize(RomManager manger, string context) {
    manger.SaveRom(true, true, RecalcChecksumBehavior.Never);
    PrintRomSize(manger, context);
}

void OptimizeCollision(RomManager manger) {
    StreamWriter collisionData = new("collisionData.txt");
    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            //if (level.LevelID != 0x22 || area.AreaID != 4) continue;
            collisionData.WriteLine($"AREA {level.LevelID} {area.AreaID}");
            
            /*if (level.LevelID == 0x6 && area.AreaID == 11) {
                Console.WriteLine(string.Join(";", (IEnumerable<Geopointer>)area.AreaModel.Fast3DBuffer.DLPointers));
                var bin = new SM64Lib.Data.BinaryFile("dump.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite) { Position = 0 };
                area.AreaModel.ToBinaryData(bin, 0, 0, 0xE000000, manger.RomConfig.CollisionBaseConfig);
                bin.Close();
            }*/
            
            Dictionary<byte, List<Triangle>> triangles = [];
            foreach (Triangle tri in area.AreaModel.Collision.Mesh.Triangles) {
                byte funny = tri.CollisionType;
                if (!triangles.ContainsKey(funny))
                    triangles.Add(funny, []);
                
                triangles[funny].Add(tri);
            }

            foreach ((byte collisionType, List<Triangle> tris) in triangles) {
                bool hasParams = manger.RomConfig.CollisionBaseConfig.CollisionTypesWithParams.Contains(collisionType);
                collisionData.WriteLine($"\tCOLLISIONTYPE {collisionType}");

                foreach (Triangle tri in tris) {
                    string ln = "\t\tTRI ";
                    foreach (Vertex v in tri.Vertices) {
                        ln += $"{v.X};{v.Y};{v.Z} ";
                    }
                    if (hasParams) {
                        foreach (byte param in tri.ColParams) {
                            ln += $"{param} ";
                        }
                    }
                    collisionData.WriteLine(ln);
                }
            }
        }
    }
    collisionData.Close();

    Process p = Process.Start("blender", "-b --python blendmaballs.py");
    p.WaitForExit();

    File.Delete("collisionData.txt");
    File.Move("collisionData.txt.new", "collisionData.txt");

    {
        StreamReader collisionDataSr = new("collisionData.txt");
        Dictionary<string, Vertex> posToVert = [];
        VertexList verts = [];
        TriangleList tris = [];
        LevelArea currentArea = null;
        byte currentCollisionType = 0;
        bool colTypeHasParams = false;

        void CommitToArea() {
            if (currentArea != null) {
                currentArea.AreaModel.Collision.Mesh.Vertices = verts;
                currentArea.AreaModel.Collision.Mesh.Triangles = tris;

                posToVert = [];
                verts = [];
                tris = [];
                currentArea = null;
            }
        }

        int lineNumber = 0;
        while (!collisionDataSr.EndOfStream) {
            lineNumber++;
            string[] ln = collisionDataSr.ReadLine().Trim().Split(" ");

            try {
                switch (ln[0]) {
                    case "AREA": {
                        CommitToArea();
                        
                        int levelID = int.Parse(ln[1]);
                        int areaID = int.Parse(ln[2]);
                        currentArea = manger.Levels.Where(l => l.LevelID == levelID).First()
                                            .Areas.Where(a => a.AreaID == areaID).First();
                    }
                        break;
                    case "COLLISIONTYPE":
                        currentCollisionType = byte.Parse(ln[1]);
                        colTypeHasParams = manger.RomConfig.CollisionBaseConfig.CollisionTypesWithParams.Contains(currentCollisionType);
                        break;
                    case "TRI": {
                        Vertex[] _verts = new Vertex[3];
                        for (int i = 1; i <= 3; i++) {
                            string v = ln[i];

                            if (!posToVert.TryGetValue(v, out Vertex vertex)) {
                                string[] components = v.Split(";");
                                vertex = new Vertex {
                                    X = short.Parse(components[0]),
                                    Y = short.Parse(components[1]),
                                    Z = short.Parse(components[2]),
                                };
                                posToVert.Add(v, vertex);
                                verts.Add(vertex);
                            }

                            _verts[i - 1] = vertex;
                        }

                        Triangle tri = new() {
                            CollisionType = currentCollisionType,
                            Vertices = _verts
                        };
                        if (colTypeHasParams) {
                            tri.ColParams[0] = byte.Parse(ln[4]);
                            tri.ColParams[1] = byte.Parse(ln[5]);
                        }
                        tris.Add(tri);
                    }
                        break;
                }
            }
            catch (Exception e) {
                Console.WriteLine("Skill issue located on line " + lineNumber + ":");
                Console.WriteLine(e);
                return;
            }
        }
        CommitToArea();
        collisionDataSr.Close();
    }
}

void OptimizeFast3D(RomManager manger, string painting64Path, Dictionary<(byte, byte), AreaPaintingCfg> painting64Cfg) {
    uint GetTextureID(uint ptr, byte texType) {
        return (ptr & 0xFFFFFF) | ((uint)texType << 24);
    }

    const int GLOBAL_TEXTURE_BANK_LIMIT = 256*1024;
    const int LOCAL_TEXTURE_BANK_LIMIT = 256*1024;

    int totalSaves = 0;
    Console.WriteLine($"Purely hypothetically speaking, by merging all duplicate textures...");
    List<(byte[] data, int frequency)> globalTextureBank = [];
    foreach (Level level in manger.Levels) {
        List<byte[]> uniqueTextures = [];

        foreach (LevelArea area in level.Areas) {
            AreaPaintingCfg areaPainting64Cfg = null;
            if (painting64Cfg.TryGetValue(((byte)level.LevelID, (byte)area.AreaID), out AreaPaintingCfg value)) {
                areaPainting64Cfg = value;
            }

            Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;

            List<(uint pointer, int length)> loads = [];
            
            foreach (Geopointer ptr in buffer.DLPointers) {
                uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                buffer.Seek(dataptr, SeekOrigin.Begin);
                //Console.WriteLine($"Load DL {dataptr:X2}");
                byte[] cmdBuffer = new byte[8];
                uint textureImage = 0;
                byte texType = 0;

                while (dataptr < buffer.Length) {
                    buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                    dataptr += 8;

                    uint loadPtr = 0;
                    int loadLength = 0;
                    // /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

                    switch (cmdBuffer[0]) {
                        case (byte)RSPCmd.DisplayList:
                            throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                        case (byte)RSPCmd.EndDisplayList:
				            goto dlEnd;
                        case (byte)RDPCmd.LoadBlock: // (contains texture size)
                            //Console.WriteLine($"gsDPLoadBlock {(ReadU16(cmdBuffer, 5) >> 4) + 1:X8}");
                            loadPtr = textureImage;
                            
                            int length = (ReadU16(cmdBuffer, 5) >> 4) + 1;
                            switch (texType & 3) {
                                case 2: // 16 bit
                                    length *= 2;
                                    break;
                                case 3: // 32 bit
                                    length *= 4;
                                    break;
                            }
                            
                            if (areaPainting64Cfg != null) {
                                if (textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF)) {
                                    length = 0; // do not load the fuck texture, let painting64 handle it
                                }
                            }

                            loadLength = length;
                            break;
                        case (byte)RDPCmd.SetTextureImage:
                            //Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
                            texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
                            textureImage = ReadU32(cmdBuffer, 4);
                            if ((textureImage & 0xFF000000) != 0x0E000000) {
                                throw new Exception($"{textureImage:X8} is NOT segment 0E!");
                            }
                            textureImage &= 0xFFFFFF;
                            //Console.WriteLine($"textureImage: {textureImage:X8}");
                            break;
                    }

                    if (loadLength > 0) {
                        if (!loads.Contains((loadPtr, loadLength))) {
                            loads.Add((loadPtr, loadLength));
                        }
                    }
                }

                dlEnd:
                continue;
            }

            // Merge intersecting data loads for optimization and to prevent issues.
            loads = loads.OrderBy((load) => load.pointer).ToList();
            for (int i = 0; i < loads.Count - 1; i++) {
                var load1 = loads[i];
                var load2 = loads[i + 1];

                if (load1.pointer + load1.length > load2.pointer) {
                    loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer + load2.length));
                    loads.RemoveAt(i + 1);
                    i--;
                }
            }

            // Now, fetch all the data we need.
            foreach ((uint pointer, int _length) in loads) {
                int length = (_length + 0xF) & ~0xF;
                byte[] data = new byte[length];
                buffer.Seek(pointer, SeekOrigin.Begin);
                buffer.Read(data, 0, length);
                bool exists = false;

                foreach (byte[] texData in uniqueTextures) {
                    if (data.SequenceEqual(texData)) {
                        exists = true;
                        break;
                    }
                }

                if (!exists) {
                    uniqueTextures.Add(data);
                }
            }
        }

        // Once gathered, add the textures to the global bank.
        foreach (byte[] texData in uniqueTextures) {
            bool exists = false;

            for (int i = 0; i < globalTextureBank.Count; i++) {
                if (globalTextureBank[i].data.SequenceEqual(texData)) {
                    globalTextureBank[i] = (globalTextureBank[i].data, globalTextureBank[i].frequency + 1);

                    exists = true;
                    break;
                }
            }

            if (!exists) {
                globalTextureBank.Add((texData, 1));
            }
        }
    }

    {
        globalTextureBank = globalTextureBank.Where(x=>x.frequency > 1).OrderBy(x => 0-(x.data.Length * (x.frequency - 1))).ToList();
        int saveable = 0;
        int ramPressure = 0;
        int ii = 0;
        for (; ii < globalTextureBank.Count; ii++) {
            int n = globalTextureBank[ii].data.Length * (globalTextureBank[ii].frequency - 1);

            if (ramPressure + globalTextureBank[ii].data.Length > GLOBAL_TEXTURE_BANK_LIMIT)
                break;
            
            saveable += n;
            if (n > 0) {
                ramPressure += globalTextureBank[ii].data.Length;
            }
        }
        while (globalTextureBank.Count > ii)
            globalTextureBank.RemoveAt(ii);
        Console.WriteLine($"Global texture bank: {ramPressure/1024} KiB (save {saveable/1024} KiB)");
        totalSaves += saveable;
    }

    foreach (Level level in manger.Levels) {
        List<(byte[] data, int frequency)> localTextureBank = [];

        foreach (LevelArea area in level.Areas) {
            AreaPaintingCfg areaPainting64Cfg = null;
            if (painting64Cfg.TryGetValue(((byte)level.LevelID, (byte)area.AreaID), out AreaPaintingCfg value)) {
                areaPainting64Cfg = value;
            }

            Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;

            List<(uint pointer, int length)> loads = [];
            List<byte[]> uniqueTextures = [];
            
            foreach (Geopointer ptr in buffer.DLPointers) {
                uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                buffer.Seek(dataptr, SeekOrigin.Begin);
                //Console.WriteLine($"Load DL {dataptr:X2}");
                byte[] cmdBuffer = new byte[8];
                uint textureImage = 0;
                byte texType = 0;

                while (dataptr < buffer.Length) {
                    buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                    dataptr += 8;

                    uint loadPtr = 0;
                    int loadLength = 0;
                    // /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

                    switch (cmdBuffer[0]) {
                        case (byte)RSPCmd.DisplayList:
                            throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                        case (byte)RSPCmd.EndDisplayList:
				            goto dlEnd;
                        case (byte)RDPCmd.LoadBlock: // (contains texture size)
                            //Console.WriteLine($"gsDPLoadBlock {(ReadU16(cmdBuffer, 5) >> 4) + 1:X8}");
                            loadPtr = textureImage;
                            
                            int length = (ReadU16(cmdBuffer, 5) >> 4) + 1;
                            switch (texType & 3) {
                                case 2: // 16 bit
                                    length *= 2;
                                    break;
                                case 3: // 32 bit
                                    length *= 4;
                                    break;
                            }
                            
                            if (areaPainting64Cfg != null) {
                                if (textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF)) {
                                    length = 0; // do not load the fuck texture, let painting64 handle it
                                }
                            }

                            loadLength = length;
                            break;
                        case (byte)RDPCmd.SetTextureImage:
                            //Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
                            texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
                            textureImage = ReadU32(cmdBuffer, 4);
                            if ((textureImage & 0xFF000000) != 0x0E000000) {
                                throw new Exception($"{textureImage:X8} is NOT segment 0E!");
                            }
                            textureImage &= 0xFFFFFF;
                            //Console.WriteLine($"textureImage: {textureImage:X8}");
                            break;
                    }

                    if (loadLength > 0) {
                        if (!loads.Contains((loadPtr, loadLength))) {
                            loads.Add((loadPtr, loadLength));
                        }
                    }
                }

                dlEnd:
                continue;
            }

            // Merge intersecting data loads for optimization and to prevent issues.
            loads = loads.OrderBy((load) => load.pointer).ToList();
            for (int i = 0; i < loads.Count - 1; i++) {
                var load1 = loads[i];
                var load2 = loads[i + 1];

                if (load1.pointer + load1.length > load2.pointer) {
                    loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer + load2.length));
                    loads.RemoveAt(i + 1);
                    i--;
                }
            }

            // Now, fetch all the data we need.
            foreach ((uint pointer, int _length) in loads) {
                int length = (_length + 0xF) & ~0xF;
                byte[] data = new byte[length];
                buffer.Seek(pointer, SeekOrigin.Begin);
                buffer.Read(data, 0, length);
                bool exists = false;

                foreach (byte[] texData in uniqueTextures) {
                    if (data.SequenceEqual(texData)) {
                        exists = true;
                        break;
                    }
                }

                if (!exists) {
                    uniqueTextures.Add(data);
                }
            }

            // Finally, add the textures to top textures.
            foreach (byte[] texData in uniqueTextures) {
                bool exists = false;

                for (int i = 0; i < localTextureBank.Count; i++) {
                    if (localTextureBank[i].data.SequenceEqual(texData)) {
                        localTextureBank[i] = (localTextureBank[i].data, localTextureBank[i].frequency + 1);

                        exists = true;
                        break;
                    }
                }

                if (!exists) {
                    localTextureBank.Add((texData, 1));
                }
            }
        }

        localTextureBank = localTextureBank.Where(x=>x.frequency > 1 && !globalTextureBank.Any(y=>y.data.SequenceEqual(x.data)))
            .OrderBy(x => 0-(x.data.Length * (x.frequency - 1))).ToList();
        int saveable = 0;
        int ramPressure = 0;
        int ii = 0;
        for (; ii < localTextureBank.Count; ii++) {
            int n = localTextureBank[ii].data.Length * (localTextureBank[ii].frequency - 1);

            if (ramPressure + localTextureBank[ii].data.Length > LOCAL_TEXTURE_BANK_LIMIT)
                break;
            
            saveable += n;
            if (n > 0) {
                ramPressure += localTextureBank[ii].data.Length;
            }
        }
        while (localTextureBank.Count > ii)
            localTextureBank.RemoveAt(ii);
        Console.WriteLine($"Local texture bank for level {level.LevelID:X2}: {ramPressure/1024} KiB (save {saveable/1024} KiB)");
        totalSaves += saveable;

        int brothersInChrist = 0;
        foreach (LevelArea area in level.Areas) {
            AreaPaintingCfg areaPainting64Cfg = null;
            if (painting64Cfg.TryGetValue(((byte)level.LevelID, (byte)area.AreaID), out AreaPaintingCfg value)) {
                areaPainting64Cfg = value;
            }

            bool verboseDebug = level.LevelID == 0x10 && area.AreaID == 5;

            Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;
            //Console.WriteLine($"OptimizeFast3D: area {level.LevelID:X2}:{area.AreaID} original size {buffer.Length:X2}");

            List<byte> newBuffer = [];
            List<(uint pointer, int length, int type)> loads = [];
            Dictionary<uint, byte[]> newData = [];
            Dictionary<uint, uint> oldToNewPtrMap = [];
            Dictionary<uint, (bool u, bool v)> clamp = [];

            int STAT_totalLoads = 0;
            int STAT_uniqueLoads = 0;
            int vertexGeometryCount = 1;

            // Scan for all data loads first.
            foreach (Geopointer ptr in buffer.DLPointers) {
                uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                buffer.Seek(dataptr, SeekOrigin.Begin);
                //Console.WriteLine($"Load DL {dataptr:X2}");
                byte[] cmdBuffer = new byte[8];
                uint textureImage = 0;
                uint texturerImager = 0;
                byte texType = 0;
                bool inGeometry = false;

                while (dataptr < buffer.Length) {
                    buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                    dataptr += 8;

                    uint loadPtr = 0;
                    int loadLength = 0;
                    int loadType = -1;
                    // /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

                    switch (cmdBuffer[0]) {
                        case (byte)RSPCmd.DisplayList:
                            if (inGeometry) {
                                inGeometry = false;
                            }
                            throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                        case (byte)RSPCmd.EndDisplayList:
                            if (inGeometry) {
                                inGeometry = false;
                            }
				            goto dlEnd;
                        case (byte)RDPCmd.LoadBlock: // (contains texture size)
                            //Console.WriteLine($"gsDPLoadBlock {(ReadU16(cmdBuffer, 5) >> 4) + 1:X8}");
                            if (inGeometry) {
                                inGeometry = false;
                            }
                            STAT_totalLoads++;
                            loadPtr = textureImage;
                            
                            int length = (ReadU16(cmdBuffer, 5) >> 4) + 1;
                            switch (texType & 3) {
                                case 2: // 16 bit
                                    length *= 2;
                                    break;
                                case 3: // 32 bit
                                    length *= 4;
                                    break;
                            }
                            
                            if (areaPainting64Cfg != null) {
                                if (textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF)) {
                                    length = 0; // do not load the fuck texture, let painting64 handle it
                                }
                            }

                            loadLength = length;
                            loadType = 1; // texture
                            break;
                        case (byte)RDPCmd.SetTextureImage:
                            //Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
                            if (inGeometry) {
                                inGeometry = false;
                            }
                            texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
                            textureImage = ReadU32(cmdBuffer, 4);
                            if ((textureImage & 0xFF000000) != 0x0E000000) {
                                throw new Exception($"{textureImage:X8} is NOT segment 0E!");
                            }
                            textureImage &= 0xFFFFFF;
                            texturerImager = textureImage;
                            //Console.WriteLine($"textureImage: {textureImage:X8}");
                            break;

                        case (byte)RDPCmd.SetTileSize: {
                            uint width = (ReadU32(cmdBuffer, 4) >> 12) & 0xFFF;
                            uint height = (ReadU32(cmdBuffer, 4) >> 0) & 0xFFF;
                            if (width > 124)
                                texturerImager |= 0x01000000;
                            if (height > 124)
                                texturerImager |= 0x02000000;
                        }
                            break;
                        
                        case (byte)RSPCmd.Vertex:
                            //Console.WriteLine($"gsSPVertex {ReadU16(cmdBuffer, 2)} {ReadU32(cmdBuffer, 4):X8}");
                            loadLength = ReadU16(cmdBuffer, 2);
                            loadPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                            loadPtr += (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));
                            
                            if (!inGeometry) {
                                inGeometry = true;
                                vertexGeometryCount++;
                            }
                            loadType = (int)texturerImager;
                            break;
                        case (byte)RSPCmd.Tri1:
                            break;
                        case 0x03: // G_MOVEMEM
                            //Console.WriteLine($"G_MOVEMEM {ReadU16(cmdBuffer, 2)} {ReadU32(cmdBuffer, 4):X8}");
                            if (inGeometry) {
                                inGeometry = false;
                            }
                            loadLength = ReadU16(cmdBuffer, 2);
                            loadPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                            loadType = 0; // lightma
                            break;
                        default:
                            if (inGeometry) {
                                inGeometry = false;
                            }
                            break;
                    }

                    if (loadLength > 0) {
                        if (!loads.Contains((loadPtr, loadLength, loadType))) {
                            STAT_uniqueLoads++;
                            loads.Add((loadPtr, loadLength, loadType));
                        }
                    }
                }

                dlEnd:
                continue;
            }

            bool tryMapPtr(uint oldPtr, out uint newPtr) {
                newPtr = 0;

                foreach ((uint _old, uint _new) in oldToNewPtrMap) {
                    int length = newData[_new].Length;

                    if (oldPtr >= _old && oldPtr - _old < length) {
                        newPtr = _new + (oldPtr - _old);
                        if ((oldPtr == 0x03C6C0 && newPtr == 0x02A6C0) || (oldPtr == 0x03C7A0 && newPtr == 0x02A7B0)) {
                            Console.WriteLine($"the mapping visione: {_new:X8} + ({oldPtr:X8} - {_old:X8})");
                        }
                        return true;
                    }
                }

                return false;
            }

            void printCmd(byte[] data, int pad) {
                if (!verboseDebug) return;

                string debugLine = $"UNKNOWN {string.Join(", ", data.Select(b => b.ToString("X2")))}";
                ConsoleColor color = ConsoleColor.Red;

                switch (data[0]) {
                    case (byte)RSPCmd.Vertex:
                        ushort loadLength = ReadU16(data, 2);
                        debugLine = $"gsSPVertex(0x{ReadU32(data, 4):X8}, {loadLength/0x10}, {data[1] & 0xF})";
                        color = ConsoleColor.Green; // dma
                        break;
                    case (byte)RSPCmd.EndDisplayList:
                        debugLine = $"gsSPEndDisplayList()";
                        color = ConsoleColor.White; // flow
                        break;
                    case (byte)RSPCmd.ClearGeometryMode:
                        debugLine = $"gsSPClearGeometryMode(0x{ReadU32(data, 4):X8})";
                        color = ConsoleColor.Blue; // configuration
                        break;
                    case (byte)RSPCmd.SetGeometryMode:
                        debugLine = $"gsSPSetGeometryMode(0x{ReadU32(data, 4):X8})";
                        color = ConsoleColor.Blue; // configuration
                        break;
                    case (byte)RSPCmd.Tri1:
                        debugLine = $"gsSP1Triangle({data[5] / 10}, {data[6] / 10}, {data[7] / 10})";
                        color = ConsoleColor.DarkGreen; // tri
                        break;

/* ok so basically
#define	gDPPipelineMode(pkt, mode)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_PIPELINE, 1, mode)
#define	gsDPPipelineMode(mode)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_PIPELINE, 1, mode)

#define	gDPSetCycleType(pkt, type)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_CYCLETYPE, 2, type)
#define	gsDPSetCycleType(type)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_CYCLETYPE, 2, type)

#define	gDPSetTexturePersp(pkt, type)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTPERSP, 1, type)
#define	gsDPSetTexturePersp(type)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTPERSP, 1, type)

#define	gDPSetTextureDetail(pkt, type)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTDETAIL, 2, type)
#define	gsDPSetTextureDetail(type)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTDETAIL, 2, type)

#define	gDPSetTextureLOD(pkt, type)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTLOD, 1, type)
#define	gsDPSetTextureLOD(type)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTLOD, 1, type)

#define	gDPSetTextureLUT(pkt, type)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTLUT, 2, type)
#define	gsDPSetTextureLUT(type)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTLUT, 2, type)

#define	gDPSetTextureFilter(pkt, type)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTFILT, 2, type)
#define	gsDPSetTextureFilter(type)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTFILT, 2, type)

#define	gDPSetTextureConvert(pkt, type)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTCONV, 3, type)
#define	gsDPSetTextureConvert(type)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTCONV, 3, type)

#define	gDPSetCombineKey(pkt, type)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_COMBKEY, 1, type)
#define	gsDPSetCombineKey(type)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_COMBKEY, 1, type)

#ifndef _HW_VERSION_1
#define	gDPSetColorDither(pkt, mode)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_RGBDITHER, 2, mode)
#define	gsDPSetColorDither(mode)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_RGBDITHER, 2, mode)
#else
#define gDPSetColorDither(pkt, mode)    \
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_COLORDITHER, 1, mode)
#define gsDPSetColorDither(mode)                \
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_COLORDITHER, 1, mode)
#endif

#ifndef _HW_VERSION_1
#define	gDPSetAlphaDither(pkt, mode)	\
	gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_ALPHADITHER, 2, mode)
#define	gsDPSetAlphaDither(mode)		\
	gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_ALPHADITHER, 2, mode)
#endif


#define	gsSPSetOtherMode(cmd, sft, len, data)				\
{{									\
	_SHIFTL(cmd, 24, 8) | _SHIFTL(sft, 8, 8) | _SHIFTL(len, 0, 8),	\
	(unsigned int)(data)						\
}}
*/
                    case (byte)RSPCmd.SetOtherModeH: {
                            uint uploadData = ReadU32(data, 4);
                            Dictionary<byte, string> functions = new() {
                                { 4, "gsDPSetAlphaDither" },
                                { 6, "gsDPSetColorDither" },
                                { 8, "gsDPSetCombineKey" },
                                { 9, "gsDPSetTextureConvert" },
                                { 12, "gsDPSetTextureFilter" },
                                { 14, "gsDPSetTextureLUT" },
                                { 16, "gsDPSetTextureLOD" },
                                { 17, "gsDPSetTextureDetail" },
                                { 19, "gsDPSetTexturePersp" },
                                { 20, "gsDPSetCycleType" },
                                { 23, "gsDPPipelineMode" },
                            };

                            if (functions.TryGetValue((byte)(data[2] >> 1), out string func)) {
                                debugLine = $"{func}(0x{uploadData:X8})";
                                color = ConsoleColor.Blue; // configuration
                            }
                            else {
                                debugLine = $"UNKNOWN SetOtherModeH {string.Join(", ", data.Select(b => b.ToString("X2")))}";
                                color = ConsoleColor.Red; // unknown
                            }
                        }
                        break;
                    
/*
# define gsSPTexture(s, t, level, tile, on)				\
{{									\
	(_SHIFTL(G_TEXTURE,24,8)|_SHIFTL(BOWTIE_VAL,16,8)|		\
	 _SHIFTL((level),11,3)|_SHIFTL((tile),8,3)|_SHIFTL((on),0,8)),	\
        (_SHIFTL((s),16,16)|_SHIFTL((t),0,16))				\
}}
*/
                    case (byte)RSPCmd.Texture:
                        debugLine = $"gsSPTexture({ReadU16(data, 4)}, {ReadU16(data, 6)}, {(data[2] >> 3) & 0x7}, {(data[2] >> 0) & 0x7}, {data[3]})";
                        color = ConsoleColor.Blue; // configuration
                        break;

/*
#define	gsDPFullSync()		gsDPNoParam(G_RDPFULLSYNC)
#define	gsDPTileSync()		gsDPNoParam(G_RDPTILESYNC)
#define	gsDPPipeSync()		gsDPNoParam(G_RDPPIPESYNC)
#define	gsDPLoadSync()		gsDPNoParam(G_RDPLOADSYNC)
*/
                    case (byte)RDPCmd.FullSync:
                        debugLine = $"gsDPFullSync()";
                        color = ConsoleColor.Magenta; // sync
                        break;
                    case (byte)RDPCmd.TileSync:
                        debugLine = $"gsDPTileSync()";
                        color = ConsoleColor.Magenta; // sync
                        break;
                    case (byte)RDPCmd.PipeSync:
                        debugLine = $"gsDPPipeSync()";
                        color = ConsoleColor.Magenta; // sync
                        break;
                    case (byte)RDPCmd.LoadSync:
                        debugLine = $"gsDPLoadSync()";
                        color = ConsoleColor.Magenta; // sync
                        break;

/*
#define gsDPLoadTileGeneric(c, tile, uls, ult, lrs, lrt)		\
{{									\
	_SHIFTL(c, 24, 8) | _SHIFTL(uls, 12, 12) | _SHIFTL(ult, 0, 12),	\
	_SHIFTL(tile, 24, 3) | _SHIFTL(lrs, 12, 12) | _SHIFTL(lrt, 0, 12)\
}}

#define	gDPSetTileSize(pkt, t, uls, ult, lrs, lrt)			\
		gDPLoadTileGeneric(pkt, G_SETTILESIZE, t, uls, ult, lrs, lrt)
#define	gsDPSetTileSize(t, uls, ult, lrs, lrt)				\
		gsDPLoadTileGeneric(G_SETTILESIZE, t, uls, ult, lrs, lrt)
#define	gDPLoadTile(pkt, t, uls, ult, lrs, lrt)				\
		gDPLoadTileGeneric(pkt, G_LOADTILE, t, uls, ult, lrs, lrt)
#define	gsDPLoadTile(t, uls, ult, lrs, lrt)				\
		gsDPLoadTileGeneric(G_LOADTILE, t, uls, ult, lrs, lrt)
*/
                    case (byte)RDPCmd.SetTileSize:
                        debugLine = $"gsDPSetTileSize({(ReadU16(data, 1) >> 4) & 0xFFF}, {ReadU16(data, 2) & 0xFFF}, {data[4]}, {(ReadU16(data, 5) >> 4) & 0xFFF}, {ReadU16(data, 6) & 0xFFF})";
                        color = ConsoleColor.Blue; // configuration
                        break;
                    // this command appears to be completely unused:fire:
                    case (byte)RDPCmd.LoadTile:
                        debugLine = $"gsDPLoadTile({(ReadU16(data, 1) >> 4) & 0xFFF}, {ReadU16(data, 2) & 0xFFF}, {data[4]}, {(ReadU16(data, 5) >> 4) & 0xFFF}, {ReadU16(data, 6) & 0xFFF})";
                        color = ConsoleColor.Green; // dma?
                        break;

/*
#define gsDPLoadBlock(tile, uls, ult, lrs, dxt)				\
{{									\
	(_SHIFTL(G_LOADBLOCK, 24, 8) | _SHIFTL(uls, 12, 12) | 		\
	 _SHIFTL(ult, 0, 12)),						\
	(_SHIFTL(tile, 24, 3) | 					\
	 _SHIFTL((MIN(lrs,G_TX_LDBLK_MAX_TXL)), 12, 12) |		\
	 _SHIFTL(dxt, 0, 12))						\
}}
*/
                    case (byte)RDPCmd.LoadBlock:
                        debugLine = $"gsDPLoadBlock({data[4]}, {(ReadU16(data, 1) >> 4) & 0xFFF}, {ReadU16(data, 2) & 0xFFF}, {(ReadU16(data, 5) >> 4) & 0xFFF}, {ReadU16(data, 6) & 0xFFF})";
                        color = ConsoleColor.Green; // dma
                        break;

/*
#define	gsDPSetTile(fmt, siz, line, tmem, tile, palette, cmt,		\
		maskt, shiftt, cms, masks, shifts)			\
{{									\
	(_SHIFTL(G_SETTILE, 24, 8) | _SHIFTL(fmt, 21, 3) | 		\
	 _SHIFTL(siz, 19, 2) | _SHIFTL(line, 9, 9) | _SHIFTL(tmem, 0, 9)),\
        (_SHIFTL(tile, 24, 3) | _SHIFTL(palette, 20, 4) | 		\
	 _SHIFTL(cmt, 18, 2) | _SHIFTL(maskt, 14, 4) | 			\
	 _SHIFTL(shiftt, 10, 4) | _SHIFTL(cms, 8, 2) | 			\
	 _SHIFTL(masks, 4, 4) | _SHIFTL(shifts, 0, 4))			\
}}
*/
                    case (byte)RDPCmd.SetTile:
                        debugLine = $"gsDPSetTile({(data[1] >> 5) & 0x7}, {(data[1] >> 3) & 0x3}, {(ReadU32(data, 0) >> 9) & 0x1FF}, {(ReadU32(data, 0) >> 0) & 0x1FF}" +
                        $", {(ReadU32(data, 4) >> 24) & 0x7}, {(ReadU32(data, 4) >> 20) & 0xF}, {(ReadU32(data, 4) >> 18) & 0x3}, {(ReadU32(data, 4) >> 14) & 0xF}, {(ReadU32(data, 4) >> 10) & 0xF}" +
                        $", {(ReadU32(data, 4) >> 8) & 0x3}, {(ReadU32(data, 4) >> 4) & 0xF}, {(ReadU32(data, 4) >> 0) & 0xF})";
                        color = ConsoleColor.Blue; // configuration
                        break;

/*
#define	gsDPSetColor(c, d)						\
{{									\
	_SHIFTL(c, 24, 8), (unsigned int)(d)				\
}}

#define	sDPRGBColor(cmd, r, g, b, a)					\
	    gsDPSetColor(cmd,						\
			 (_SHIFTL(r, 24, 8) | _SHIFTL(g, 16, 8) | 	\
			  _SHIFTL(b, 8, 8) | _SHIFTL(a, 0, 8)))

#define	gsDPSetEnvColor(r, g, b, a)					\
            sDPRGBColor(G_SETENVCOLOR, r,g,b,a)
#define	gsDPSetBlendColor(r, g, b, a)					\
            sDPRGBColor(G_SETBLENDCOLOR, r,g,b,a)
#define	gsDPSetFogColor(r, g, b, a)					\
            sDPRGBColor(G_SETFOGCOLOR, r,g,b,a)
#define	gsDPSetFillColor(d)						\
            gsDPSetColor(G_SETFILLCOLOR, (d))
*/
                    case (byte)RDPCmd.SetFillColor:
                        debugLine = $"gsDPSetFillColor(0x{ReadU32(data, 4):X8})";
                        color = ConsoleColor.Cyan; // color
                        break;
                    case (byte)RDPCmd.SetFogColor:
                        debugLine = $"gsDPSetFogColor(0x{ReadU8(data, 4):X2}, 0x{ReadU8(data, 5):X2}, 0x{ReadU8(data, 6):X2}, 0x{ReadU8(data, 7):X2})";
                        color = ConsoleColor.Cyan; // color
                        break;
                    case (byte)RDPCmd.SetBlendColor:
                        debugLine = $"gsDPSetBlendColor(0x{ReadU8(data, 4):X2}, 0x{ReadU8(data, 5):X2}, 0x{ReadU8(data, 6):X2}, 0x{ReadU8(data, 7):X2})";
                        color = ConsoleColor.Cyan; // color
                        break;
                    case (byte)RDPCmd.SetEnvColor:
                        debugLine = $"gsDPSetEnvColor(0x{ReadU8(data, 4):X2}, 0x{ReadU8(data, 5):X2}, 0x{ReadU8(data, 6):X2}, 0x{ReadU8(data, 7):X2})";
                        color = ConsoleColor.Cyan; // color
                        break;

/*
#define	GCCc0w0(saRGB0, mRGB0, saA0, mA0)				\
		(_SHIFTL((saRGB0), 20, 4) | _SHIFTL((mRGB0), 15, 5) | 	\
		 _SHIFTL((saA0), 12, 3) | _SHIFTL((mA0), 9, 3))

#define	GCCc1w0(saRGB1, mRGB1)						\
		(_SHIFTL((saRGB1), 5, 4) | _SHIFTL((mRGB1), 0, 5))

#define GCCc0w1(sbRGB0, aRGB0, sbA0, aA0)				\
                (_SHIFTL((sbRGB0), 28, 4) | _SHIFTL((aRGB0), 15, 3) |	\
		 _SHIFTL((sbA0), 12, 3) | _SHIFTL((aA0), 9, 3))

#define	GCCc1w1(sbRGB1, saA1, mA1, aRGB1, sbA1, aA1)			\
		(_SHIFTL((sbRGB1), 24, 4) | _SHIFTL((saA1), 21, 3) |	\
		 _SHIFTL((mA1), 18, 3) | _SHIFTL((aRGB1), 6, 3) |	\
		 _SHIFTL((sbA1), 3, 3) | _SHIFTL((aA1), 0, 3))

#define	gsDPSetCombineLERP(a0, b0, c0, d0, Aa0, Ab0, Ac0, Ad0,		\
		a1, b1, c1, d1,	Aa1, Ab1, Ac1, Ad1)			\
{{									\
	_SHIFTL(G_SETCOMBINE, 24, 8) |					\
	_SHIFTL(GCCc0w0(G_CCMUX_##a0, G_CCMUX_##c0,			\
		       G_ACMUX_##Aa0, G_ACMUX_##Ac0) |			\
	       GCCc1w0(G_CCMUX_##a1, G_CCMUX_##c1), 0, 24),		\
	(unsigned int)(GCCc0w1(G_CCMUX_##b0, G_CCMUX_##d0,		\
			       G_ACMUX_##Ab0, G_ACMUX_##Ad0) |		\
		       GCCc1w1(G_CCMUX_##b1, G_ACMUX_##Aa1,		\
			       G_ACMUX_##Ac1, G_CCMUX_##d1,		\
			       G_ACMUX_##Ab1, G_ACMUX_##Ad1))		\
}}
*/
                    case (byte)RDPCmd.SetCombine: {
                        uint word0 = ReadU32(data, 0);
                        uint word1 = ReadU32(data, 4);

                        // Decode GCCc0w0 from word0 (bits 0-23)
                        int saRGB0 = (int)((word0 >> 20) & 0xF);
                        int mRGB0 = (int)((word0 >> 15) & 0x1F);
                        int saA0 = (int)((word0 >> 12) & 0x7);
                        int mA0 = (int)((word0 >> 9) & 0x7);

                        // Decode GCCc1w0 from word0 (bits 24-31)
                        int saRGB1 = (int)((word0 >> 5) & 0xF);
                        int mRGB1 = (int)(word0 & 0x1F);

                        // Decode GCCc0w1 from word1 (bits 0-31)
                        int sbRGB0 = (int)((word1 >> 28) & 0xF);
                        int aRGB0 = (int)((word1 >> 15) & 0x7);
                        int sbA0 = (int)((word1 >> 12) & 0x7);
                        int aA0 = (int)((word1 >> 9) & 0x7);

                        // Decode GCCc1w1 from word1 (bits 0-31)
                        int sbRGB1 = (int)((word1 >> 24) & 0xF);
                        int saA1 = (int)((word1 >> 21) & 0x7);
                        int mA1 = (int)((word1 >> 18) & 0x7);
                        int aRGB1 = (int)((word1 >> 6) & 0x7);
                        int sbA1 = (int)((word1 >> 3) & 0x7);
                        int aA1 = (int)(word1 & 0x7);

                        debugLine = $"gsDPSetCombineLERP({saRGB0}, {mRGB0}, {saA0}, {mA0}, {sbRGB0}, {aRGB0}, {sbA0}, {aA0}, " +
                                        $"{saRGB1}, {mRGB1}, {sbRGB1}, {saA1}, {mA1}, {aRGB1}, {sbA1}, {aA1})";
                        color = ConsoleColor.Blue; // configuration
                    }
                        break;

/*
#define	gsSetImage(cmd, fmt, siz, width, i)				\
{{									\
	_SHIFTL(cmd, 24, 8) | _SHIFTL(fmt, 21, 3) |			\
	_SHIFTL(siz, 19, 2) | _SHIFTL((width)-1, 0, 12),		\
	(uintptr_t)(i)						\
}}
#define	gsDPSetTextureImage(f, s, w, i)	gsSetImage(G_SETTIMG, f, s, w, i)
*/
                    case (byte)RDPCmd.SetTextureImage:
                        debugLine = $"gsDPSetTextureImage({(data[1] >> 5) & 0x7}, {(data[1] >> 3) & 0x3}, {ReadU16(data, 2)}, 0x{ReadU32(data, 4):X8})";
                        color = ConsoleColor.Blue; // configuration
                        break;
                }

                Console.ForegroundColor = color;
                if (pad == -1)
                    Console.WriteLine(debugLine);
                else
                    Console.Write(debugLine.PadRight(pad, ' '));
            }

            // Merge intersecting data loads for optimization and to prevent issues.
            loads = loads.OrderBy((load) => load.pointer).ToList();
            for (int i = 0; i < loads.Count - 1; i++) {
                var load1 = loads[i];
                var load2 = loads[i + 1];

                if (load1.pointer + load1.length > load2.pointer) {
                    loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer - load1.pointer + load2.length), load1.type);
                    loads.RemoveAt(i + 1);
                    i--;
                }
            }

            // Merge consecutive data loads of similar data.
            for (int i = 0; i < loads.Count - 1; i++) {
                var load1 = loads[i];
                var load2 = loads[i + 1];

                if (load1.pointer + load1.length >= load2.pointer && load1.type > 1 && load1.type == load2.type) {
                    loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer - load1.pointer + load2.length), load1.type);
                    loads.RemoveAt(i + 1);
                    i--;
                }
            }

            // Now, fetch all the data we need.
            foreach ((uint pointer, int length, int type) in loads) {
                byte[] data = new byte[length];
                buffer.Seek(pointer, SeekOrigin.Begin);
                buffer.Read(data, 0, length);
                byte[] pad = new byte[0x10 - (length & 0xF)];
                bool exists = false;

                if (type > 1 && (length & 0xF) == 0) {
                    // vertex load,
                    // see if we can like unshit the uvs on this or something
                    Vtx_tn[] vertices = new Vtx_tn[length / 0x10];
                    for (int i = 0; i < vertices.Length; i++) {
                        vertices[i] = Vtx_tn.Read(data, (uint)(i * 0x10));
                    }

                    uint texPtr = (uint)(type & 0xFFFFFF);
                    bool wide = (type & 0x01000000) != 0;
                    bool tall = (type & 0x02000000) != 0;

                    const short UV_SNAP = 1024;
                    const short UV_SNAP_MARGIN = 128;
                    const short UV_HALF_PIXEL = 16;
                    const short U_OFFSET = -16;
                    const short V_OFFSET = -16;

                    short uMin = vertices.Min(v => v.texX);
                    short uMax = vertices.Max(v => v.texX);
                    short vMin = vertices.Min(v => v.texY);
                    short vMax = vertices.Max(v => v.texY);

                    if (uMin < uMax - (UV_SNAP - UV_SNAP_MARGIN * 2) && vMin < vMax - (UV_SNAP - UV_SNAP_MARGIN * 2)) {
                        short uMinNew = uMin;
                        short uMaxNew = uMax;
                        short vMinNew = vMin;
                        short vMaxNew = vMax;

                        short dist = (short)uvmod(uMinNew, UV_SNAP);
                        if (dist > UV_SNAP / 2)
                            dist -= UV_SNAP;
                        if (Math.Abs(dist) < UV_SNAP_MARGIN) {
                            uMinNew = (short)(uMinNew - dist + UV_HALF_PIXEL + U_OFFSET);
                        }

                        dist = (short)uvmod(uMaxNew, UV_SNAP);
                        if (dist > UV_SNAP / 2)
                            dist -= UV_SNAP;
                        if (Math.Abs(dist) < UV_SNAP_MARGIN) {
                            uMaxNew = (short)(uMaxNew - dist - UV_HALF_PIXEL + U_OFFSET);
                        }

                        dist = (short)uvmod(vMinNew, UV_SNAP);
                        if (dist > UV_SNAP / 2)
                            dist -= UV_SNAP;
                        if (Math.Abs(dist) < UV_SNAP_MARGIN) {
                            vMinNew = (short)(vMinNew - dist + UV_HALF_PIXEL + V_OFFSET);
                        }

                        dist = (short)uvmod(vMaxNew, UV_SNAP);
                        if (dist > UV_SNAP / 2)
                            dist -= UV_SNAP;
                        if (Math.Abs(dist) < UV_SNAP_MARGIN) {
                            vMaxNew = (short)(vMaxNew - dist - UV_HALF_PIXEL + V_OFFSET);
                        }

                        short wideEnd = (short)(UV_SNAP * (wide ? 2 : 1) - UV_HALF_PIXEL * 2);
                        short tallEnd = (short)(UV_SNAP * (tall ? 2 : 1) - UV_HALF_PIXEL * 2);
                        if ((uMaxNew - uMinNew) == wideEnd) {
                            // how convenient...
                            uMinNew = UV_HALF_PIXEL + U_OFFSET;
                            uMaxNew = wideEnd;
                        }
                        if ((vMaxNew - vMinNew) == tallEnd) {
                            // how convenient...
                            vMinNew = UV_HALF_PIXEL + U_OFFSET;
                            vMaxNew = tallEnd;
                        }

                        for (int i = 0; i < vertices.Length; i++) {
                            Vtx_tn v = vertices[i];
                            if (uMin != uMinNew || uMax != uMaxNew)
                                v.texX = (short)Math.Round(Remap(v.texX, uMin, uMax, uMinNew, uMaxNew));
                            if (vMin != vMinNew || vMax != vMaxNew)
                                v.texY = (short)Math.Round(Remap(v.texY, vMin, vMax, vMinNew, vMaxNew));
                            vertices[i] = v;
                        }

                        (bool u, bool v) clampma = (false, false);
                        if (uMinNew == UV_HALF_PIXEL + U_OFFSET && uMaxNew == wideEnd) {
                            clampma.u = true;
                        }
                        if (vMinNew == UV_HALF_PIXEL + U_OFFSET && vMaxNew == tallEnd) {
                            clampma.v = true;
                        }
                        if (clamp.ContainsKey(texPtr))
                            clamp[texPtr] = (false, false);
                        else
                            clamp.Add(texPtr, clampma);
                    }

                    for (int i = 0; i < vertices.Length; i++) {
                        byte[] bytes = vertices[i].ToBytes();
                        Array.Copy(bytes, 0, data, i * 0x10, 0x10);
                    }
                }

                foreach (KeyValuePair<uint, byte[]> dataEntry in newData) {
                    if (data.SequenceEqual(dataEntry.Value)) {
                        exists = true;
                        oldToNewPtrMap.Add(pointer, dataEntry.Key);
                        break;
                    }
                }

                if (!exists) {
                    uint newPtr = (uint)newBuffer.Count;
                    newBuffer.AddRange(data);
                    if (pad.Length < 0x10)
                        newBuffer.AddRange(pad);

                    newData.Add(newPtr, data);
                    oldToNewPtrMap.Add(pointer, newPtr);
                }
            }
            if (areaPainting64Cfg != null) {
                uint newPtr = (uint)newBuffer.Count;
                byte[] fuck = new byte[areaPainting64Cfg.paintingCount * 0x80];
                newBuffer.AddRange(fuck);
                newData.Add(newPtr, fuck);
                oldToNewPtrMap.Add(areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF, newPtr);
            }

            //Console.WriteLine($"Loads: {STAT_totalLoads} total, {STAT_uniqueLoads} unique.");
            //Console.WriteLine($"New data size: {newBuffer.Count:X2}");

            // We have everything, construct display lists now.
            foreach (Geopointer ptr in buffer.DLPointers) {
                uint startptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                uint dataptr = startptr;
                byte[] cmdBuffer = new byte[8];
                byte[] newCmdBuffer = new byte[8];

                // Geometry scan, this optimizes rom manager's gsSPVertex and gsSP1Triangle visione
                Dictionary<uint, byte[]> geometryBalls = [];
                //if (level.LevelID == 0x10 && area.AreaID == 5)
                {
                    // Step 1: extract every gsSPVertex and gsSP1Triangle call.
                    List<List<(uint ptr, byte[] cmd)>> drawCommandmas = [];
                    {
                        List<(uint ptr, byte[] cmd)> drawCommands = [];
                        uint vertPtr = 0;
                        buffer.Seek(dataptr, SeekOrigin.Begin);
                        while (dataptr < buffer.Length) {
                            buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                            dataptr += 8;

                            switch (cmdBuffer[0]) {
                                case (byte)RSPCmd.DisplayList:
                                    throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                                case (byte)RSPCmd.EndDisplayList:
                                    if (drawCommands.Count > 0)
                                        drawCommandmas.Add(drawCommands);
                                    goto __dlEnd;

                                case (byte)RSPCmd.Vertex: {
                                    byte[] newBuf = new byte[8];
                                    Array.Copy(cmdBuffer, newBuf, cmdBuffer.Length);
                                    drawCommands.Add((dataptr - 8, newBuf));

                                    vertPtr = ReadU32(newBuf, 4) & 0xFFFFFF;
                                }
                                    break;
                                case (byte)RSPCmd.Tri1: {
                                    byte[] newBuf = new byte[8];
                                    Array.Copy(cmdBuffer, newBuf, cmdBuffer.Length);

                                    uint v1i = cmdBuffer[5] / 10u, v2i = cmdBuffer[6] / 10u, v3i = cmdBuffer[7] / 10u, flag = cmdBuffer[4];
                                    if (v1i == v2i || v1i == v3i || v2i == v3i) {
                                        // yes, somehow there's even these type of obvious 0 area triangles
                                        gSPNoOp(newBuf);
                                        geometryBalls.Add(dataptr - 8, newBuf);
                                        break;
                                    }
                                    // Read vertices from buffer
                                    Vtx_tn v1 = Vtx_tn.Read(buffer, vertPtr + (v1i * 0x10));
                                    Vtx_tn v2 = Vtx_tn.Read(buffer, vertPtr + (v2i * 0x10));
                                    Vtx_tn v3 = Vtx_tn.Read(buffer, vertPtr + (v3i * 0x10));
                                    //if (verboseDebug)
                                    //    Console.Write($"Area: {TriArea(v1, v2, v3)}");

                                    if (IsTriDegenerate(v1, v2, v3)) {
                                        gSPNoOp(newBuf);
                                        geometryBalls.Add(dataptr - 8, newBuf);
                                        break;
                                    }

                                    drawCommands.Add((dataptr - 8, newBuf));
                                }
                                    break;
                                default:
                                    if (drawCommands.Count > 0)
                                        drawCommandmas.Add(drawCommands);
                                    drawCommands = [];
                                    break;
                            }
                        }
                    }

                    __dlEnd:
                    // Step 2: search for possible reductions
                    foreach (List<(uint ptr, byte[] cmd)> drawCommands in drawCommandmas) {
                        int lastVertexLoadIndex = -1;
                        uint vertexPtr = 0;
                        uint vertexCount = 0;

                        int debugballs = 0;
                        for (int i = 0; i < drawCommands.Count; i++) {
                            uint currentCmdPtr = drawCommands[i].ptr;
                            Array.Copy(drawCommands[i].cmd, cmdBuffer, cmdBuffer.Length);

                            switch (cmdBuffer[0]) {
                                case (byte)RSPCmd.Vertex: {
                                    ushort loadLength = ReadU16(cmdBuffer, 2);
                                    uint loadedVertexPtr, newVertexPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                                    uint loadedVertexCount, newVertexCount = loadLength / 0x10u;
                                    int newVertexLoadIndex = i;
                                    loadedVertexPtr = newVertexPtr;
                                    loadedVertexCount = newVertexCount;

                                    if (newVertexPtr == vertexPtr + vertexCount * 0x10 && vertexCount <= 13) {
                                        debugballs--;
                                        /*if (newVertexPtr == 0x06C4C0 && newVertexCount == 9) {
                                            debugballs = 5;
                                            Console.WriteLine($"debugma 5 balls.");
                                        }*/
                                        if (debugballs > 0) {
                                            Console.WriteLine($"\tvertexPtr: 0x0E{vertexPtr:X6}");
                                            Console.WriteLine($"\tvertexCount: {vertexCount}");
                                            Console.WriteLine($"\tnewVertexPtr (just loaded): 0x0E{newVertexPtr:X6}");
                                        }
                                        // Perform lookahead scan.
                                        // If we can borrow (at least some of) the next vertex load's triangles
                                        // by simply loading more vertices in the first place, then we should.
                                        byte[] _cmdBuffer = new byte[8];
                                        int j;

                                        if (debugballs > 0) {
                                            Console.WriteLine("scan1");
                                        }
                                        bool perfectMerge = false;
                                        for (j = i + 1; j < drawCommands.Count; j++) {
                                            if (debugballs > 0) {
                                                Console.Write($"{drawCommands[j].ptr:X8}: ");
                                                printCmd(drawCommands[j].cmd, -1);
                                                Console.ResetColor();
                                            }
                                            Array.Copy(drawCommands[j].cmd, _cmdBuffer, _cmdBuffer.Length);

                                            switch (_cmdBuffer[0]) {
                                                case (byte)RSPCmd.Tri1:
                                                    int v1 = _cmdBuffer[5] / 10, v2 = _cmdBuffer[6] / 10, v3 = _cmdBuffer[7] / 10;

                                                    if (debugballs > 0) {
                                                        Console.WriteLine($"{vertexCount} + Math.Max({v1}, Math.Max({v2}, {v3})) >= 16");
                                                    }
                                                    if (vertexCount + Math.Max(v1, Math.Max(v2, v3)) >= 16) {
                                                        if (debugballs > 0) {
                                                            Console.WriteLine("break condition: vertexCount");
                                                        }
                                                        goto _search1end;
                                                    }
                                                    break;
                                                case (byte)RSPCmd.Vertex:
                                                    if (debugballs > 0) {
                                                        Console.WriteLine("break condition: vertex command");
                                                    }
                                                    perfectMerge = true;
                                                    goto _search1end;
                                            }
                                        }
                                        _search1end:
                                        j--; // to get the last valid command

                                        if (debugballs > 0) {
                                            Console.WriteLine("writema balls");
                                        }
                                        for (int k = i + 1; k <= j; k++) {
                                            Array.Copy(drawCommands[k].cmd, _cmdBuffer, _cmdBuffer.Length);
                                            if (_cmdBuffer[0] != (byte)RSPCmd.Tri1) continue;

                                            uint v1 = _cmdBuffer[5] / 10u, v2 = _cmdBuffer[6] / 10u, v3 = _cmdBuffer[7] / 10u, flag = _cmdBuffer[4];
                                            if (newVertexPtr != vertexPtr) {
                                                newVertexPtr = vertexPtr;
                                                newVertexCount = 0;
                                                newVertexLoadIndex = lastVertexLoadIndex;
                                            }
                                            newVertexCount = Math.Max(newVertexCount, vertexCount + 1 + Math.Max(v1, Math.Max(v2, v3)));
                                            if (debugballs > 0) {
                                                Console.WriteLine($"\tnewVertexPtr: 0x0E{newVertexPtr:X6}");
                                                Console.WriteLine($"\tnewVertexCount: {newVertexCount}");
                                            }

                                            gSP1Triangle(drawCommands[k - 1].cmd, vertexCount + v1, vertexCount + v2, vertexCount + v3, flag);
                                            if (debugballs > 0) {
                                                Console.Write($"{drawCommands[k - 1].ptr:X8} = ");
                                                printCmd(drawCommands[k - 1].cmd, -1);
                                                Console.ResetColor();
                                            }
                                        }
                                        if (j > i) {
                                            uint vertexOffset = 16;
                                            // These 2 loops start at the first invalid command, which is either a triangle or a vertex.
                                            // We're only interested if they're triangles, we want to reduce the vertex indices in this case
                                            for (int k = j + 1; k < drawCommands.Count; k++) {
                                                Array.Copy(drawCommands[k].cmd, _cmdBuffer, _cmdBuffer.Length);

                                                switch (_cmdBuffer[0]) {
                                                    case (byte)RSPCmd.Tri1:
                                                        uint v1 = _cmdBuffer[5] / 10u, v2 = _cmdBuffer[6] / 10u, v3 = _cmdBuffer[7] / 10u;

                                                        vertexOffset = Math.Min(vertexOffset, Math.Min(v1, Math.Min(v2, v3)));
                                                        break;
                                                    case (byte)RSPCmd.Vertex:
                                                        goto _search2end;
                                                }
                                            }
                                            _search2end:
                                            for (int k = j + 1; k < drawCommands.Count; k++) {
                                                Array.Copy(drawCommands[k].cmd, _cmdBuffer, _cmdBuffer.Length);

                                                switch (_cmdBuffer[0]) {
                                                    case (byte)RSPCmd.Tri1:
                                                        uint v1 = _cmdBuffer[5] / 10u, v2 = _cmdBuffer[6] / 10u, v3 = _cmdBuffer[7] / 10u, flag = _cmdBuffer[4];

                                                        gSP1Triangle(drawCommands[k].cmd, v1 - vertexOffset, v2 - vertexOffset, v3 - vertexOffset, flag);
                                                        if (debugballs > 0) {
                                                            Console.Write($"{drawCommands[k].ptr:X8} = ");
                                                            printCmd(drawCommands[k].cmd, -1);
                                                            Console.ResetColor();
                                                        }
                                                        break;
                                                    case (byte)RSPCmd.Vertex:
                                                        goto _search3end;
                                                }
                                            }
                                            _search3end:
                                            vertexOffset &= 0xF;
                                            // Then, we insert the new vertex load command :D
                                            if (perfectMerge || (j >= drawCommands.Count - 1 || drawCommands[j + 1].cmd[0] != (byte)RSPCmd.Tri1))
                                                gSPNoOp(drawCommands[j].cmd);
                                            else
                                                gSPVertex(drawCommands[j].cmd, /*newVertexPtr + newVertexCount * 0x10*/ loadedVertexPtr + vertexOffset * 0x10, loadedVertexCount - vertexOffset, 0);
                                            if (debugballs > 0) {
                                                Console.Write($"{drawCommands[j].ptr:X8} = ");
                                                printCmd(drawCommands[j].cmd, -1);
                                                Console.ResetColor();
                                            }
                                            gSPVertex(drawCommands[lastVertexLoadIndex].cmd, newVertexPtr, newVertexCount, 0);
                                            if (debugballs > 0) {
                                                Console.Write($"{drawCommands[lastVertexLoadIndex].ptr:X8} = ");
                                                printCmd(drawCommands[lastVertexLoadIndex].cmd, -1);
                                                Console.ResetColor();
                                            }
                                        }
                                    }

                                    vertexPtr = newVertexPtr;
                                    vertexCount = newVertexCount;
                                    lastVertexLoadIndex = newVertexLoadIndex;
                                }
                                    break;
                            }
                        }

                        // post analysis to make sure shit isn't being fucked
                        if (verboseDebug) {
                            Console.WriteLine("where is my ass?");
                        }

                        int vertMin = 0, vertMax = 0;
                        uint vertPtr = 0;
                        foreach ((uint ptr, byte[] cmd) bal in drawCommands) {
                            if (verboseDebug) {
                                printCmd(bal.cmd, 40);
                                Console.ResetColor();
                            }
                            
                            switch (bal.cmd[0]) {
                                case (byte)RSPCmd.Vertex: {
                                    /* (was chatgpt note ig this can be helpful for whoever reads or something)
                                    This command takes vertex data at vertPtr, skips vertMin vertices, and loads *((u16*)(cmd + 2)) bytes of data.
                                    Each vertex is 0x10 bytes long and uses the following C struct:
 *
 * Vertex (set up for use with normals)
 * 
typedef struct {
	short		ob[3];	 * x, y, z *  
	unsigned short	flag;
	short		tc[2];	 * texture coord * 
	signed char	n[3];	 * normal * 
	unsigned char   a;       * alpha  * 
} Vtx_tn; // this is a 16 (0x10) byte struct
                                    Vertex data can be obtained using a prior loaded variable:
                                    `Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;`
                                    which implements C# MemoryStream (seek and fetch bytes).

                                    I have only created ReadU8, ReadU16, ReadU32, ReadU64 functions. Converting to signed requires (short)ReadU16()
                                    */


                                    vertMin = ReadU8(bal.cmd, 1) & 0xF;
                                    vertMax = ReadU16(bal.cmd, 2) / 0x10 + vertMin;
                                    vertPtr = ReadU32(bal.cmd, 4) & 0xFFFFFF;
                                }
                                break;
                                case (byte)RSPCmd.Tri1: {
                                    uint v1i = bal.cmd[5] / 10u, v2i = bal.cmd[6] / 10u, v3i = bal.cmd[7] / 10u, flag = bal.cmd[4];
                                    if (v1i < vertMin || v1i >= vertMax || v2i < vertMin || v2i >= vertMax || v3i < vertMin || v3i >= vertMax)
                                        throw new Exception("Bad vertex load");
                                    
                                    /*if (v1i == v2i || v1i == v3i || v2i == v3i) {
                                        // yes, somehow there's even these type of obvious 0 area triangles
                                        gSPNoOp(bal.cmd);
                                    }
                                    // Read vertices from buffer
                                    Vtx_tn v1 = Vtx_tn.Read(buffer, vertPtr, (int)v1i);
                                    Vtx_tn v2 = Vtx_tn.Read(buffer, vertPtr, (int)v2i);
                                    Vtx_tn v3 = Vtx_tn.Read(buffer, vertPtr, (int)v3i);
                                    //if (verboseDebug)
                                    //    Console.Write($"Area: {TriArea(v1, v2, v3)}");

                                    if (IsTriDegenerate(v1, v2, v3)) {
                                        gSPNoOp(bal.cmd);
                                    }*/
                                }
                                break;
                            }
                            if (verboseDebug)
                                Console.WriteLine();
                        }

                        // finally we geometry the balls
                        foreach ((uint ptr, byte[] cmd) bal in drawCommands) {
                            geometryBalls.Add(bal.ptr, bal.cmd);
                        }
                    }
                }

                dataptr = startptr;
                buffer.Seek(dataptr, SeekOrigin.Begin);
                Dictionary<uint, List<byte>> newTextureCmds = [];
                List<byte> currentList = null;
                byte texType = 0;
                bool thefunnytol = false;

                void printAndAddDL(byte[] data) {
                    printCmd(data, -1);
                    /*Console.WriteLine("Writing DL");
                    for (int i = 0; i < data.Length; i += 8) {
                        Console.WriteLine($"{ReadU32(data, i):X8} {ReadU32(data, i + 4):X8}");
                    }*/

                    newBuffer.AddRange(data);
                }

                bool currentListContainsCmd(byte cmd) {
                    if (currentList == null) return false;
                    for (int i = 0; i < currentList.Count; i += 8) {
                        if (currentList[i] == cmd) return true;
                    }
                    return false;
                }

                void flushTextureCmds() {
                    foreach (KeyValuePair<uint, List<byte>> texture in newTextureCmds) {
                        /*if (areaPainting64Cfg != null) {
                            uint baseTexID = GetTextureID(areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF, 2);
                            if (oldToNewTexMap.TryGetValue(baseTexID, out uint val)) {
                                if (val == texture.Key) {
                                    // Do not render the out of bounds placeholder texture
                                    Console.WriteLine("Skipping FUCK");
                                    continue;
                                }
                            }
                        }*/
                        printAndAddDL(texture.Value.ToArray());
                    }

                    newTextureCmds.Clear();
                    currentList = null;
                }

                Dictionary<string, ulong> rcpState = [];
                Dictionary<string, ulong> rcpStateKnownBits = [];
                byte envColorA = 255;
                // Returns whether any state was modified.
                bool RcpSetState(string name, ulong value, ulong mask) {
                    if (!rcpState.ContainsKey(name))
                        rcpState.Add(name, 0);
                    if (!rcpStateKnownBits.ContainsKey(name))
                        rcpStateKnownBits.Add(name, 0);

                    value &= mask; // just to make sure
                    /*if (verboseDebug) {
                        Console.WriteLine($"Write {name} 0x{value:X8} mask 0x{mask:X8}");
                        Console.WriteLine($"Current state {name} 0x{value:X8} mask 0x{mask:X8}");
                    }*/
                    if ((rcpStateKnownBits[name] & mask) == mask) {
                        if ((rcpState[name] & mask) == value) {
                            return false;
                        }
                    }

                    rcpState[name] = (rcpState[name] & ~mask) | (value & mask);
                    rcpStateKnownBits[name] |= mask;
                    return true;
                }

                bool WillBeOverwrittenBeforeGeometry(ulong mask) {
                    bool returnValue = false;
                    uint seekBack = dataptr;
                    byte command = cmdBuffer[0];

                    byte[] _cmdBuffer = new byte[8];
                    while (dataptr < buffer.Length) {
                        buffer.Read(_cmdBuffer, 0, _cmdBuffer.Length);
                        dataptr += 8;

                        switch (cmdBuffer[0]) {
                            case (byte)RSPCmd.Tri1:
                            case (byte)RSPCmd.EndDisplayList:
                                returnValue = false;
                                goto ret;
                        }
                        if (cmdBuffer[0] == command) {
                            ulong newMask = 0;
                            switch (command) {
                                case (byte)RSPCmd.SetGeometryMode:
                                case (byte)RSPCmd.ClearGeometryMode:
                                    newMask = ReadU32(cmdBuffer, 4);
                                    break;
                                case (byte)RSPCmd.SetOtherModeH:
                                    newMask = (uint)((1 << cmdBuffer[2]) - 1) << cmdBuffer[3];
                                    break;

                                case (byte)RDPCmd.SetTileSize:
                                    newMask = 0x00FFFFFFFFFFFFFF;
                                    break;
                                case (byte)RDPCmd.SetTile:
                                    newMask = 0x00FFFFFFFFFFFFFF;
                                    break;

                                default:
                                //    Console.WriteLine($"Unknown DL command {cmdBuffer[0]:X2}, may alter state. Flushing texture buffers now!");
                                //    flushTextureCmds();
                                    break;
                            }

                            mask &= ~newMask;
                            if (mask == 0) {
                                returnValue = true;
                                goto ret;
                            }
                        }
                    }

                    ret:
                    dataptr = seekBack;
                    buffer.Seek(dataptr, SeekOrigin.Begin);
                    return returnValue;
                }


                ptr.SegPointer = 0x0E000000 | newBuffer.Count;
                if (verboseDebug) {
                    Console.Write("IN".PadRight(40, ' '));
                    Console.WriteLine("OUT");
                }
                uint currentTex = 0;
                while (dataptr < buffer.Length) {
                    buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                    printCmd(cmdBuffer, 50);
                    if (geometryBalls.TryGetValue(dataptr, out byte[] overrideCmd)) {
                        Array.Copy(overrideCmd, cmdBuffer, cmdBuffer.Length);
                    }
                    Array.Copy(cmdBuffer, newCmdBuffer, cmdBuffer.Length);
                    dataptr += 8;
                    bool skipCmd = false;
                    // /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

                    switch (cmdBuffer[0]) {
                        case (byte)RSPCmd.NOOP:
                        case (byte)RDPCmd.NOOP:
                            skipCmd = true; // should be obvious why
                            break;
                        case 0x03: { // G_MOVEMEM
                            uint lightPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;

                            if (!tryMapPtr(lightPtr, out uint val)) {
                                throw new Exception($"G_MOVEMEM with unmapped ptr {lightPtr:X2}??");
                            }

                            WriteU32(newCmdBuffer, 4, val | 0x0E000000);
                        }
                            break;
                        case (byte)RSPCmd.Vertex: {
                            //thefunnytol = Random.Shared.NextDouble() < 0.8;
                            skipCmd = thefunnytol;

                            if (!skipCmd) {
                                uint vertexPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                                vertexPtr += (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));

                                if (!tryMapPtr(vertexPtr, out uint val)) {
                                    throw new Exception($"gsSPVertex with unmapped ptr {vertexPtr:X2}??");
                                }

                                val -= (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));
                                //if (verboseDebug)
                                //    Console.WriteLine($"mapping {vertexPtr | 0x0E000000:X8} -> {val | 0x0E000000:X8}");
                                WriteU32(newCmdBuffer, 4, val | 0x0E000000);
                            }
                        }
                            break;
                        case (byte)RSPCmd.DisplayList:
                            throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                        case (byte)RSPCmd.EndDisplayList:
                            flushTextureCmds();
                            printAndAddDL(cmdBuffer);
				            goto dlEnd;
                        case (byte)RDPCmd.SetEnvColor:
                            envColorA = ReadU8(cmdBuffer, 7);
                            currentTex = 0;
                            thefunnytol = envColorA <= 1;
                            break;
                        case (byte)RDPCmd.SetTextureImage: {
                            texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
                            uint textureImage = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                            currentTex = textureImage;
                            //uint textureID = GetTextureID(textureImage, texType);

                            thefunnytol = envColorA <= 1;
                            if (areaPainting64Cfg != null) {
                                thefunnytol = textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF) ||
                                    areaPainting64Cfg.textureSegmentedAddresses.Any(addr => textureImage == (addr & 0xFFFFFF));
                            }

                            if (!tryMapPtr(textureImage, out uint val)) {
                                throw new Exception($"gsDPSetTextureImage with unmapped ptr {textureImage:X2}??");
                            }

                            //if (!newTextureCmds.ContainsKey(val))
                            //    newTextureCmds.Add(val, []);
                            //Console.WriteLine($"Entering texture {val:X8}");
                            //currentList = newTextureCmds[val];

                            //WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
                            WriteU32(newCmdBuffer, 4, (val & 0x00FFFFFF) | 0x0E000000);
                            /* TODO: CI4 conversion
                            if (texType == ((2 << 2) | 0)) {
                                Console.WriteLine("Conversion to CI4 in effect!");
                                Console.WriteLine($"{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");
                                Console.WriteLine($"{string.Join("", newCmdBuffer.Select(b => b.ToString("X2")))}");
                            }*/
                            skipCmd = thefunnytol || currentListContainsCmd(0xFD);
                        }
                            break;
                        case (byte)RSPCmd.Tri1:
                            skipCmd = thefunnytol;
                            break;

                        case (byte)RSPCmd.SetGeometryMode:
                            skipCmd = !RcpSetState("SPGeometryMode", 0xFFFFFFFF, ReadU32(cmdBuffer, 4));
                            break;
                        case (byte)RSPCmd.ClearGeometryMode:
                            skipCmd = !RcpSetState("SPGeometryMode", 0x00000000, ReadU32(cmdBuffer, 4));
                            break;
                        case (byte)RSPCmd.SetOtherModeH:
                            skipCmd = !RcpSetState("SPOtherModeH", ReadU32(cmdBuffer, 4), (uint)((1 << cmdBuffer[2]) - 1) << cmdBuffer[3]);
                            break;

                        case (byte)RDPCmd.FullSync:
                        case (byte)RDPCmd.LoadSync:
                        case (byte)RDPCmd.PipeSync:
                        case (byte)RDPCmd.TileSync:
                        case (byte)RDPCmd.LoadBlock: // (contains texture size)
                            //skipCmd = currentListContainsCmd((byte)RDPCmd.LoadBlock);
                            skipCmd = thefunnytol;
                            break;

                        case (byte)RDPCmd.SetTileSize:
                            skipCmd = !RcpSetState("DPTileSize", ((ulong)(ReadU32(cmdBuffer, 0) & 0xFFFFFF) << 32) | ReadU32(cmdBuffer, 4), 0x00FFFFFFFFFFFFFF);
                            break;
                        case (byte)RDPCmd.SetTile:
                            /* TODO: CI4 conversion
                            WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
                            if (texType == ((2 << 2) | 0)) {
                                WriteU8(newCmdBuffer, 2, (byte)(ReadU8(cmdBuffer, 2) / 2));
                            }*/
                            if (clamp.TryGetValue(currentTex, out (bool u, bool v) clampma)) {
                                if (clampma.u) {
                                    cmdBuffer[6] |= 0x2;
                                    newCmdBuffer[6] |= 0x2;
                                }
                                if (clampma.v) {
                                    cmdBuffer[5] |= 0x2 << 2;
                                    newCmdBuffer[5] |= 0x2 << 2;
                                }
                            }

                            skipCmd = !RcpSetState("DPTile", ((ulong)(ReadU32(cmdBuffer, 0) & 0xFFFFFF) << 32) | ReadU32(cmdBuffer, 4), 0x00FFFFFFFFFFFFFF);
                            break;

                        default:
                        //    Console.WriteLine($"Unknown DL command {cmdBuffer[0]:X2}, may alter state. Flushing texture buffers now!");
                        //    flushTextureCmds();
                            break;
                    }

                    if (!skipCmd) {
                        if (currentList == null)
                            printAndAddDL(newCmdBuffer);
                        else
                            currentList.AddRange(newCmdBuffer);
                    }
                    else if (verboseDebug) {
                        Console.WriteLine();
                    }
                }

                dlEnd:
                Console.ResetColor();
                continue;
            }

            // Check these moving texture objects
            for (int i = 0; i < area.ScrollingTextures.Count; i++) {
                ManagedScrollingTexture scrollingTexture = area.ScrollingTextures[i];

                //Console.WriteLine($"Texture scroll object - points to {scrollingTexture.VertexPointer:X8}");

                if (!tryMapPtr((uint)scrollingTexture.VertexPointer & 0xFFFFFF, out uint val)) {
                    throw new Exception($"Texture scroll object with unmapped ptr {scrollingTexture.VertexPointer:X8}??");
                }

                scrollingTexture.VertexPointer = (int)(val | 0x0E000000);
            }

            buffer.SetLength(newBuffer.Count);
            buffer.Position = 0;
            foreach (byte b in newBuffer)
                buffer.WriteByte(b);
            
            if (areaPainting64Cfg != null) {
                Console.WriteLine($"pains are canon");
                List<uint> trol = [ .. areaPainting64Cfg.textureSegmentedAddresses, areaPainting64Cfg.baseSegmentedAddress ];
                // Update Painting64 config.
                foreach (uint oldPtr in trol) {
                    List<string> oldStr = [
                        $"0x0E{oldPtr & 0xFFFFFF:X6}".ToLowerInvariant(),
                        $"0xE{oldPtr & 0xFFFFFF:X6}".ToLowerInvariant(),
                    ];
                    if (!tryMapPtr(oldPtr & 0xFFFFFF, out uint newPtr)) {
                        throw new Exception($"areaPainting64Cfg contains unmapped ptr {oldPtr:X8}?? mappings:\n{string.Join("\n", oldToNewPtrMap.Select(mapping => $"{mapping.Key:X8} -> {mapping.Value:X8} len: {(newData.TryGetValue(mapping.Value, out byte[] data) ? data.Length : 0):X8}"))}");
                    }
                    string newStr = $"0x00E{newPtr & 0xFFFFFF:X6}";
                    Console.WriteLine($"mapma {oldStr[0]} -> {newStr}");
                    foreach (string _oldStr in oldStr)
                        areaPainting64Cfg.config = areaPainting64Cfg.config.ToLowerInvariant().Replace(_oldStr, newStr);
                }
            }
        }
        Console.WriteLine($"sisters in christ: {brothersInChrist}");
    }
    Console.WriteLine($"We've hypothetically saved a grand total of {totalSaves/1024} KiB!");

    StreamWriter sw = new("paintingcfg.txt");
    foreach (KeyValuePair<(byte, byte), AreaPaintingCfg> kvp in painting64Cfg) {
        sw.WriteLine($"LEVEL_ID={kvp.Key.Item1}");
        sw.WriteLine(kvp.Value.config);
    }
    sw.Close();
}

void OptimizeObjects(RomManager manger) {
    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            Console.WriteLine($"OptimizeObjects: area {level.LevelID:X2}:{area.AreaID} {area.Objects.Count}");
            for (int i = area.Objects.Count - 1; i >= 0; i--) {
                LevelscriptCommand objCmd = area.Objects[i];

                if (objCmd.CommandType == LevelscriptCommandTypes.Normal3DObject) {
                    byte[] data = objCmd.ToArray();
                    if (data[2] == 0) {
                        Console.WriteLine("Removing object (acts = 0)");
                        area.Objects.RemoveAt(i);
                    }

/*
                    uint bhv = ReadU32(data, 0x14);
                    if (bhv == 0x1F002C00) {
                        Console.WriteLine("Mirror object!");
                    }
*/
                }
            }
            Console.WriteLine($"new object count: {area.Objects.Count}");
        }
    }
}




void EvaporateCollision(RomManager manger) {
    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            area.AreaModel.Collision.Mesh.Vertices = [];
            area.AreaModel.Collision.Mesh.Triangles = [];
        }
    }
}

void EvaporateFast3D(RomManager manger, string painting64Path, Dictionary<(byte, byte), AreaPaintingCfg> painting64Cfg) {
    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;
            buffer.SetLength(0);
        }
    }
}

void EvaporateObjects(RomManager manger) {
    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            for (int i = area.Objects.Count - 1; i >= 0; i--) {
                LevelscriptCommand objCmd = area.Objects[i];

                if (objCmd.CommandType == LevelscriptCommandTypes.Normal3DObject) {
                    area.Objects.RemoveAt(i);
                }
            }
        }
    }
}


void MIO0_Fast3D(RomManager manger) {
    int maxMio0Size = 0;

    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;
            byte[] newBuffer = new byte[buffer.Length];

            buffer.Seek(0, SeekOrigin.Begin);
            buffer.Read(newBuffer, 0, (int)buffer.Length);

            // mio0
            {
                string fbin = $"tmp.bin";
                string fmio0 = $"tmp.mio0";
                File.WriteAllBytes(fbin, newBuffer);

                RunProcess($"tools/mio0 \"{fbin}\" \"{fmio0}\"");

                newBuffer = File.ReadAllBytes(fmio0);
                if (newBuffer.Length > maxMio0Size) {
                    maxMio0Size = newBuffer.Length;
                }
            }

            buffer.SetLength(newBuffer.Length);
            buffer.Position = 0;
            foreach (byte b in newBuffer)
                buffer.WriteByte(b);
        }
    }
    Console.WriteLine($"Largest mio0 data size: {maxMio0Size/1024} KiB!");
}


/*RomManager manger = new("b3313 silved.z64");
manger.LoadRom();
Console.WriteLine(manger.GameName);


Console.WriteLine($"there are {manger.Levels.Length} levels");

foreach (var level in manger.Levels)
{
    Console.WriteLine($"level {level.LevelID} has areas:");
    foreach (var area in level.Areas)
    {
        Console.WriteLine($"area {area.AreaID} has warps: ");
        Console.WriteLine(area.Warps);
    }
}*/
RomManager manger;
Console.WriteLine("hmm");
if (args[1] == "MIO0_STAGE") {
    manger = new(args[0]);
    manger.LoadRom();
    MIO0_Fast3D(manger);
    SaveAndPrintRomSize(manger, "post Fast3D mio0 compression");
    return;
}

Dictionary<(byte, byte), AreaPaintingCfg> painting64Cfg = [];
AreaPaintingCfg current = null;
StreamReader sr = new(args[2]);
byte levelID = 0;
int linen = 0;
try {
    bool addedTexHalf2 = false;
    while (!sr.EndOfStream) {
        string ln = sr.ReadLine();
        linen++;

        string key = ln.Split('=')[0].ToLowerInvariant();
        if (key == "new_painting") {
            current.paintingCount++;
            if (current != null && current.textureSegmentedAddresses.Count > 0 && !addedTexHalf2) {
                current.textureSegmentedAddresses.Add(current.textureSegmentedAddresses[^1] + 0x1000);
            }
            addedTexHalf2 = false;
        }
        else if (ln.Length > key.Length + 1) {
            string strValue = ln.Substring(key.Length + 1);
            string value = strValue;

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                try {
                    ulong u = Convert.ToUInt64(value.Substring(2), 16);
                    value = u.ToString();
                }
                catch { }
            }

            switch (key) {
                case "level_id":
                    current = null;
                    levelID = byte.Parse(value);
                break;
                case "area_id":
                    byte areaID = byte.Parse(value);
                    if (!painting64Cfg.ContainsKey((levelID, areaID))) {
                        painting64Cfg.Add((levelID, areaID), new AreaPaintingCfg());
                    }
                    current = painting64Cfg[(levelID, areaID)];
                    break;
                case "base_rom_address":
                    throw new Exception("base_rom_address in Painting64 config not supported!");
                case "base_segmented_address":
                    current.baseSegmentedAddress = uint.Parse(value);
                    break;
                case "rom_address":
                    throw new Exception("painting rom_address in Painting64 config not supported!");
                case "segmented_address":
                    throw new Exception("painting segmented_address in Painting64 config not supported!");
                case "texture_segmented_address":
                    current.textureSegmentedAddresses.Add(uint.Parse(value));
                    break;
                case "texture_segmented_address_half2":
                    addedTexHalf2 = true;
                    current.textureSegmentedAddresses.Add(uint.Parse(value));
                    break;
            }
        }

        if (current != null) {
            current.config += ln + "\n";
        }
    }

    sr.Close();
}
catch (Exception e) {
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Exception at line {linen} in paintingcfg.txt:\n\n{e}");
    sr.Close();
    Console.ReadKey(true);
    return;
}
Console.WriteLine("Loaded Painting64 config!");

manger = new(args[0]);
string painting64Path = args[1];
Console.WriteLine("a manger?");
manger.LoadRom();
PrintRomSize(manger, "pre compression");

//OptimizeCollision(manger);
//SaveAndPrintRomSize(manger, "post collision optimization");
//EvaporateCollision(manger);
//SaveAndPrintRomSize(manger, "post collision evaporation");

OptimizeFast3D(manger, painting64Path, painting64Cfg);
SaveAndPrintRomSize(manger, "post Fast3D optimization");
//EvaporateFast3D(manger, painting64Path, painting64Cfg);
//SaveAndPrintRomSize(manger, "post Fast3D evaporation");

//OptimizeObjects(manger);
//EvaporateObjects(manger);
//SaveAndPrintRomSize(manger, "post object purge");

if (File.Exists("paintingcfg.txt")) {
    Console.WriteLine($"Applying new Painting64 cfg to rom...");
    string romPath = manger.RomFile;
    manger = null;
    Process p = Process.Start(painting64Path, $"\"{romPath}\" --automatic");
    p.WaitForExit();
    File.Delete("paintingcfg.txt");
}

RunProcess($"./BeeieOptimizer \"{args[0]}\" MIO0_STAGE");