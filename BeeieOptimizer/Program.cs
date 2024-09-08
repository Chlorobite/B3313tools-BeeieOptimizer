using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SM64Lib;
using SM64Lib.Geolayout;
using SM64Lib.Levels;
using SM64Lib.Levels.Script;
using SM64Lib.Levels.ScrolTex;
using SM64Lib.Model.Collision;
using SM64Lib.Model.Fast3D;
using Z.Core.Extensions;


byte ReadU8(byte[] buffer, int offset) {
    return buffer[offset];
}
ushort ReadU16(byte[] buffer, int offset) {
    return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
}
uint ReadU32(byte[] buffer, int offset) {
    return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
}
ulong ReadU64(byte[] buffer, int offset) {
    return ((ulong)buffer[offset] << 56) | ((ulong)buffer[offset + 1] << 48) | ((ulong)buffer[offset + 2] << 40) | ((ulong)buffer[offset + 3] << 32) |
            ((ulong)buffer[offset + 4] << 24) | ((ulong)buffer[offset + 5] << 16) | ((ulong)buffer[offset + 6] << 8) | buffer[offset + 7];
}
void WriteU8(byte[] buffer, int offset, byte value) {
    buffer[offset] = value;
}
void WriteU16(byte[] buffer, int offset, ushort value) {
    offset += 2;
    for (int i = 0; i < 2; i++) {
        buffer[--offset] = (byte)value;
        value >>= 8;
    }
}
void WriteU32(byte[] buffer, int offset, uint value) {
    offset += 4;
    for (int i = 0; i < 4; i++) {
        buffer[--offset] = (byte)value;
        value >>= 8;
    }
}
void WriteU64(byte[] buffer, int offset, ulong value) {
    offset += 8;
    for (int i = 0; i < 8; i++) {
        buffer[--offset] = (byte)value;
        value >>= 8;
    }
}

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

        foreach (LevelArea area in level.Areas) {
            AreaPaintingCfg areaPainting64Cfg = null;
            if (painting64Cfg.TryGetValue(((byte)level.LevelID, (byte)area.AreaID), out AreaPaintingCfg value)) {
                areaPainting64Cfg = value;
            }

            Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;
            //Console.WriteLine($"OptimizeFast3D: area {level.LevelID:X2}:{area.AreaID} original size {buffer.Length:X2}");

            List<byte> newBuffer = [];
            List<(uint pointer, int length)> loads = [];
            Dictionary<uint, byte[]> newData = [];
            Dictionary<uint, uint> oldToNewPtrMap = [];

            int STAT_totalLoads = 0;
            int STAT_uniqueLoads = 0;

            // Scan for all data loads first.
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
                        case (byte)RSPCmd.Vertex:
                            //Console.WriteLine($"gsSPVertex {ReadU16(cmdBuffer, 2)} {ReadU32(cmdBuffer, 4):X8}");
                            loadLength = ReadU16(cmdBuffer, 2);
                            loadPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                            loadPtr += (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));
                            break;
                        case 0x03: // G_MOVEMEM
                            //Console.WriteLine($"G_MOVEMEM {ReadU16(cmdBuffer, 2)} {ReadU32(cmdBuffer, 4):X8}");
                            loadLength = ReadU16(cmdBuffer, 2);
                            loadPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                            break;
                    }

                    if (loadLength > 0) {
                        if (!loads.Contains((loadPtr, loadLength))) {
                            STAT_uniqueLoads++;
                            loads.Add((loadPtr, loadLength));
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
                        return true;
                    }
                }

                return false;
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
            foreach ((uint pointer, int length) in loads) {
                byte[] data = new byte[length];
                buffer.Seek(pointer, SeekOrigin.Begin);
                buffer.Read(data, 0, length);
                byte[] pad = new byte[0x10 - (length & 0xF)];
                bool exists = false;

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
                oldToNewPtrMap.Add((areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF), newPtr);
            }

            //Console.WriteLine($"Loads: {STAT_totalLoads} total, {STAT_uniqueLoads} unique.");
            //Console.WriteLine($"New data size: {newBuffer.Count:X2}");

            // We have everything, construct display lists now.
            foreach (Geopointer ptr in buffer.DLPointers) {
                uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                buffer.Seek(dataptr, SeekOrigin.Begin);
                Dictionary<uint, List<byte>> newTextureCmds = [];
                List<byte> currentList = null;
                byte[] cmdBuffer = new byte[8];
                byte[] newCmdBuffer = new byte[8];
                byte texType = 0;
                bool thefunnytol = false;

                void printAndAddDL(byte[] data) {
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

                ptr.SegPointer = 0x0E000000 | newBuffer.Count;
                while (dataptr < buffer.Length) {
                    buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                    Array.Copy(cmdBuffer, newCmdBuffer, cmdBuffer.Length);
                    dataptr += 8;
                    bool skipCmd = false;
                    // /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

                    switch (cmdBuffer[0]) {
                        case 0x03: { // G_MOVEMEM
                            uint lightPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;

                            if (!tryMapPtr(lightPtr, out uint val)) {
                                throw new Exception($"G_MOVEMEM with unmapped ptr {lightPtr:X2}??");
                            }

                            WriteU32(newCmdBuffer, 4, val | 0x0E000000);
                        }
                            break;
                        case (byte)RSPCmd.Vertex: {
                            thefunnytol = Random.Shared.NextDouble() < 0.8;
                            skipCmd = thefunnytol;

                            if (!skipCmd) {
                                uint vertexPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                                vertexPtr += (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));

                                if (!tryMapPtr(vertexPtr, out uint val)) {
                                    throw new Exception($"gsSPVertex with unmapped ptr {vertexPtr:X2}??");
                                }

                                val -= (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));
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
                        case (byte)RDPCmd.LoadBlock: // (contains texture size)
                            skipCmd = currentListContainsCmd(0xF3);
                            break;
                        case (byte)RDPCmd.SetTile:
                            /* TODO: CI4 conversion
                            WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
                            if (texType == ((2 << 2) | 0)) {
                                WriteU8(newCmdBuffer, 2, (byte)(ReadU8(cmdBuffer, 2) / 2));
                            }*/
                            break;
                        case (byte)RDPCmd.SetTextureImage: {
                            texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
                            uint textureImage = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                            //uint textureID = GetTextureID(textureImage, texType);

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
                            skipCmd = currentListContainsCmd(0xFD);
                        }
                            break;
                        case 0xBF: // gsSP1Triangle
                            skipCmd = thefunnytol;
                            break;
                        // Commands known to not alter state or require any pointer processing
                        case 0xBA: // G_SETOTHERMODE_H
                        case 0xE6: // gsDPLoadSync
                        case 0xE7: // gsDPPipeSync
                        case 0xE8: // gsDPTileSync
                        case 0xE9: // gsDPFullSync
                        case 0xF2: // gsDPSetTileSize
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
                }

                dlEnd:
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
                // Update Painting64 config.
                foreach (KeyValuePair<uint, uint> tex in oldToNewPtrMap) {
                    List<string> oldStr = [
                        $"0x0E{tex.Key & 0xFFFFFF:X6}".ToLowerInvariant(),
                        $"0xE{tex.Key & 0xFFFFFF:X6}".ToLowerInvariant(),
                    ];
                    string newStr = $"0x0E{tex.Value & 0xFFFFFF:X6}";
                    foreach (string _oldStr in oldStr)
                        areaPainting64Cfg.config = areaPainting64Cfg.config.ToLowerInvariant().Replace(_oldStr, newStr);
                }
            }
        }
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

OptimizeCollision(manger);
SaveAndPrintRomSize(manger, "post collision optimization");
//EvaporateCollision(manger);
//SaveAndPrintRomSize(manger, "post collision evaporation");

OptimizeFast3D(manger, painting64Path, painting64Cfg);
SaveAndPrintRomSize(manger, "post Fast3D optimization");
//EvaporateFast3D(manger, painting64Path, painting64Cfg);
//SaveAndPrintRomSize(manger, "post Fast3D evaporation");

OptimizeObjects(manger);
//EvaporateObjects(manger);
SaveAndPrintRomSize(manger, "post object purge");

if (File.Exists("paintingcfg.txt")) {
    Console.WriteLine($"Applying new Painting64 cfg to rom...");
    string romPath = manger.RomFile;
    manger = null;
    Process p = Process.Start(painting64Path, $"\"{romPath}\" --automatic");
    p.WaitForExit();
    File.Delete("paintingcfg.txt");
}

RunProcess($"./BeeieOptimizer \"{args[0]}\" MIO0_STAGE");