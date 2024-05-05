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

    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            AreaPaintingCfg areaPainting64Cfg = null;
            if (painting64Cfg.TryGetValue(((byte)level.LevelID, (byte)area.AreaID), out AreaPaintingCfg value)) {
                areaPainting64Cfg = value;
            }

            Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;
            Console.WriteLine($"OptimizeFast3D: area {level.LevelID:X2}:{area.AreaID} original size {buffer.Length:X2}");

            List<byte> newBuffer = [];
            Dictionary<uint, byte[]> newTextures = [];
            Dictionary<uint, byte> newTexTypeMap = [];
            Dictionary<uint, uint> oldToNewTexMap = [];

            int STAT_totalLoads = 0;
            int STAT_uniqueLoads = 0;
            int STAT_newLoads = 0;

            // Scan for texture data first.
            foreach (Geopointer ptr in buffer.DLPointers) {
                uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                buffer.Seek(dataptr, SeekOrigin.Begin);
                Console.WriteLine($"Load DL {dataptr:X2}");
                byte[] cmdBuffer = new byte[8];
                uint textureImage = 0;
                uint textureID = 0;
                byte texType = 0;

                while (dataptr < buffer.Length) {
                    buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                    dataptr += 8;
                    // /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

                    switch (cmdBuffer[0]) {
                        case 0x06: // gsSPDisplayList
                            throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                        case 0xB8: // gsSPEndDisplayList
				            goto dlEnd;
                        case 0xF3: // gsDPLoadBlock (texture size)
                            Console.WriteLine($"gsDPLoadBlock {(ReadU16(cmdBuffer, 5) >> 4) + 1:X8}");
                            STAT_totalLoads++;
                            if (!oldToNewTexMap.ContainsKey(textureID)) {
                                STAT_uniqueLoads++;
                                int length = (ReadU16(cmdBuffer, 5) >> 4) + 1;
                                switch (texType & 3) {
                                    case 2: // 16 bit
                                        length *= 2;
                                        break;
                                    case 3: // 32 bit
                                        length *= 4;
                                        break;
                                }
                                length = (length + 0xF) & ~0xF;

                                if (areaPainting64Cfg != null) {
                                    if (textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF)) {
                                        // There's no need to fetch the rest of the texture
                                        length = areaPainting64Cfg.paintingCount * 0x80;
                                    }
                                }

                                byte[] texData = new byte[length];
                                buffer.Seek(textureImage, SeekOrigin.Begin);
                                buffer.Read(texData, 0, length);
                                buffer.Seek(dataptr, SeekOrigin.Begin);

                                /* TODO: CI4 conversion
                                if (texType == 2 && length >= 64*2) { // RGBA16
                                    // If possible, perform RGBA16->CI4 conversion.
                                    bool ci4Impossible = false;
                                    List<ushort> colors = [];
                                    byte[] indices = new byte[length / 2];

                                    for (int i = 0; i < texData.Length; i += 2) {
                                        ushort color = ReadU16(texData, i);
                                        if (!colors.Contains(color)) {
                                            colors.Add(color);

                                            if (colors.Count > 16) {
                                                ci4Impossible = true;
                                                break;
                                            }
                                        }
                                        indices[i / 2] = (byte)colors.IndexOf(color);
                                    }

                                    if (!ci4Impossible) {
                                        byte[] ci4Data = new byte[length / 2];
                                        int ci4DataPtr = 0;

                                        for (int i = 0; i < indices.Length; i += 2) {
                                            ci4Data[ci4DataPtr++] = (byte)((indices[i] << 4) | indices[i + 1]);
                                        }
                                        foreach (ushort color in colors) {
                                            WriteU16(ci4Data, ci4DataPtr, color);
                                            ci4DataPtr += 2;
                                        }

                                        texData = ci4Data;
                                        texType = (2 << 2) | 0; // CI, 4 bit size
                                    }
                                }*/

                                bool exists = false;

                                foreach (KeyValuePair<uint, byte[]> texEntry in newTextures) {
                                    if ((texEntry.Key & 0xFF000000) == (textureID & 0xFF000000)
                                    && texData.SequenceEqual(texEntry.Value)) {
                                        exists = true;
                                        oldToNewTexMap.Add(textureID, texEntry.Key);
                                        break;
                                    }
                                }

                                if (!exists) {
                                    uint texturePtr = (uint)newBuffer.Count;
                                    newBuffer.AddRange(texData);

                                    uint newTexID = (texturePtr & 0xFFFFFF) | (textureID & 0xFF000000);
                                    newTextures.Add(newTexID, texData);
                                    newTexTypeMap.Add(newTexID, texType);
                                    oldToNewTexMap.Add(textureID, newTexID);
                                    STAT_newLoads++;
                                }
                            }
                            break;
                        case 0xFD: // gsDPSetTextureImage
                            Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
                            texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
                            textureImage = ReadU32(cmdBuffer, 4);
                            if ((textureImage & 0xFF000000) != 0x0E000000) {
                                throw new Exception($"{textureImage:X8} is NOT segment 0E!");
                            }
                            textureImage &= 0xFFFFFF;
                            textureID = GetTextureID(textureImage, texType);
                            Console.WriteLine($"textureImage: {textureImage:X8}");
                            Console.WriteLine($"textureID: {textureID:X8}");
                            break;
                    }
                }

                dlEnd:
                continue;
            }

            Console.WriteLine($"Loads: {STAT_totalLoads} total, {STAT_uniqueLoads} unique. Should have {STAT_newLoads} loads now");
            Console.WriteLine($"New texture buffer data length: {newBuffer.Count:X2}");

            // Now scan for vertex data.
            Dictionary<uint, byte[]> newVertices = [];
            Dictionary<uint, uint> oldToNewVertMap = [];

            foreach (Geopointer ptr in buffer.DLPointers) {
                uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                buffer.Seek(dataptr, SeekOrigin.Begin);
                byte[] cmdBuffer = new byte[8];

                while (dataptr < buffer.Length) {
                    buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                    dataptr += 8;
                    // /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

                    switch (cmdBuffer[0]) {
                        case 0x04: // gsSPVertex
                            Console.WriteLine($"gsSPVertex {ReadU16(cmdBuffer, 2)} {ReadU32(cmdBuffer, 4):X8}");
                            int length = ReadU16(cmdBuffer, 2);
                            uint vertexPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;

                            if (!oldToNewVertMap.ContainsKey(vertexPtr)) {
                                byte[] vertData = new byte[length];
                                buffer.Seek(vertexPtr, SeekOrigin.Begin);
                                buffer.Read(vertData, 0, length);
                                buffer.Seek(dataptr, SeekOrigin.Begin);
                                bool exists = false;

                                foreach (KeyValuePair<uint, byte[]> vertEntry in newVertices) {
                                    if (vertData.SequenceEqual(vertEntry.Value)) {
                                        exists = true;
                                        oldToNewVertMap.Add(vertexPtr, vertEntry.Key);
                                        break;
                                    }
                                }

                                if (!exists) {
                                    uint newVertexPtr = (uint)newBuffer.Count;
                                    newBuffer.AddRange(vertData);

                                    newVertices.Add(newVertexPtr, vertData);
                                    oldToNewVertMap.Add(vertexPtr, newVertexPtr);
                                }
                            }
                            break;
                        case 0x06: // gsSPDisplayList
                            throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                        case 0xB8: // gsSPEndDisplayList
				            goto dlEnd;
                    }
                }

                dlEnd:
                continue;
            }

            // Now scan for G_MOVEMEM data. (mainly lighting, but may be other stuff too)
            Dictionary<uint, byte[]> newLights = [];
            Dictionary<uint, uint> oldToNewLightMap = [];

            foreach (Geopointer ptr in buffer.DLPointers) {
                uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                buffer.Seek(dataptr, SeekOrigin.Begin);
                byte[] cmdBuffer = new byte[8];

                while (dataptr < buffer.Length) {
                    buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
                    dataptr += 8;
                    // /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

                    switch (cmdBuffer[0]) {
                        case 0x03: // G_MOVEMEM
                            Console.WriteLine($"G_MOVEMEM {ReadU16(cmdBuffer, 2)} {ReadU32(cmdBuffer, 4):X8}");
                            int length = ReadU16(cmdBuffer, 2);
                            uint memPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;

                            if (!oldToNewLightMap.ContainsKey(memPtr)) {
                                byte[] lightData = new byte[length];
                                buffer.Seek(memPtr, SeekOrigin.Begin);
                                buffer.Read(lightData, 0, length);
                                buffer.Seek(dataptr, SeekOrigin.Begin);
                                bool exists = false;

                                foreach (KeyValuePair<uint, byte[]> lightEntry in newLights) {
                                    if (lightData.SequenceEqual(lightEntry.Value)) {
                                        exists = true;
                                        oldToNewLightMap.Add(memPtr, lightEntry.Key);
                                        break;
                                    }
                                }

                                if (!exists) {
                                    uint newMemPtr = (uint)newBuffer.Count;
                                    newBuffer.AddRange(lightData);

                                    newLights.Add(newMemPtr, lightData);
                                    oldToNewLightMap.Add(memPtr, newMemPtr);
                                }
                            }
                            break;
                        case 0x06: // gsSPDisplayList
                            throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                        case 0xB8: // gsSPEndDisplayList
				            goto dlEnd;
                    }
                }

                dlEnd:
                continue;
            }

            // We have everything, construct display lists now.
            foreach (Geopointer ptr in buffer.DLPointers) {
                uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
                buffer.Seek(dataptr, SeekOrigin.Begin);
                Dictionary<uint, List<byte>> newTextureCmds = [];
                List<byte> currentList = null;
                byte[] cmdBuffer = new byte[8];
                byte[] newCmdBuffer = new byte[8];
                byte texType = 0;

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

                            if (!oldToNewLightMap.TryGetValue(lightPtr, out uint val)) {
                                throw new Exception($"G_MOVEMEM with unmapped ptr {lightPtr:X2}??");
                            }

                            WriteU32(newCmdBuffer, 4, val | 0x0E000000);
                        }
                            break;
                        case 0x04: { // gsSPVertex
                            uint vertexPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;

                            if (!oldToNewVertMap.TryGetValue(vertexPtr, out uint val)) {
                                throw new Exception($"gsSPVertex with unmapped ptr {vertexPtr:X2}??");
                            }

                            WriteU32(newCmdBuffer, 4, val | 0x0E000000);
                        }
                            break;
                        case 0x06: // gsSPDisplayList
                            throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
                        case 0xB8: // gsSPEndDisplayList
                            flushTextureCmds();
                            printAndAddDL(cmdBuffer);
				            goto dlEnd;
                        case 0xF3: // gsDPLoadBlock (texture size)
                            skipCmd = currentListContainsCmd(0xF3);
                            break;
                        case 0xF5: // gsDPSetTile
                            /* TODO: CI4 conversion
                            WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
                            if (texType == ((2 << 2) | 0)) {
                                WriteU8(newCmdBuffer, 2, (byte)(ReadU8(cmdBuffer, 2) / 2));
                            }*/
                            break;
                        case 0xFD: { // gsDPSetTextureImage
                            texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
                            uint textureImage = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
                            uint textureID = GetTextureID(textureImage, texType);

                            if (!oldToNewTexMap.TryGetValue(textureID, out uint val)) {
                                throw new Exception($"gsDPSetTextureImage with unmapped texID {textureID:X2}??");
                            }

                            //if (!newTextureCmds.ContainsKey(val))
                            //    newTextureCmds.Add(val, []);
                            //Console.WriteLine($"Entering texture {val:X8}");
                            //currentList = newTextureCmds[val];
                            texType = newTexTypeMap[val];

                            WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
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
                        // Commands known to not alter state or require any pointer processing
                        case 0xBA: // G_SETOTHERMODE_H
                        case 0xBF: // gsSP1Triangle
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

            buffer.SetLength(newBuffer.Count);
            buffer.Position = 0;
            foreach (byte b in newBuffer)
                buffer.WriteByte(b);
            
            if (areaPainting64Cfg != null) {
                // Update Painting64 config.
                foreach (KeyValuePair<uint, uint> tex in oldToNewTexMap) {
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


Console.WriteLine("hmm");
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

RomManager manger = new(args[0]);
string painting64Path = args[1];
Console.WriteLine("a manger?");
manger.LoadRom();
PrintRomSize(manger, "pre compression");

OptimizeCollision(manger);
SaveAndPrintRomSize(manger, "post collision optimization");

OptimizeFast3D(manger, painting64Path, painting64Cfg);
SaveAndPrintRomSize(manger, "post Fast3D optimization");

OptimizeObjects(manger);
SaveAndPrintRomSize(manger, "post object purge");

if (File.Exists("paintingcfg.txt")) {
    Console.WriteLine($"Applying new Painting64 cfg to rom...");
    string romPath = manger.RomFile;
    manger = null;
    Process p = Process.Start(painting64Path, $"\"{romPath}\"");
    p.WaitForExit();
    File.Delete("paintingcfg.txt");
}