
public enum RSPCmd: byte {
    NOOP = 0x00,
    Vertex = 0x04,
    DisplayList = 0x06,
    ClearGeometryMode = 0xB6,
    SetGeometryMode = 0xB7,
    EndDisplayList = 0xB8,
    SetOtherModeH = 0xBA,
    Texture = 0xBB,
    Tri1 = 0xBF,
}

public enum RDPCmd: byte {
    NOOP = 0xC0,
    LoadSync = 0xE6,
    PipeSync = 0xE7,
    TileSync = 0xE8,
    FullSync = 0xE9,
    SetTileSize = 0xF2,
    LoadBlock = 0xF3,
    LoadTile = 0xF4,
    SetTile = 0xF5,
    SetFillColor = 0xF7,
    SetFogColor = 0xF8,
    SetBlendColor = 0xF9,
    SetPrimColor = 0xFA,
    SetEnvColor = 0xFB,
    SetCombine = 0xFC,
    SetTextureImage = 0xFD,
    
}