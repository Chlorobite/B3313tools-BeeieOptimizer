
enum RSPCmd: byte {
    Vertex = 0x04,
    DisplayList = 0x06,
    EndDisplayList = 0xB8,
}

enum RDPCmd: byte {
    LoadBlock = 0xF3,
    SetTile = 0xF5,
    SetTextureImage = 0xFD,
}