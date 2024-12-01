using System.Collections.Generic;

class AreaPaintingCfg {
    public string config = "";
    public uint baseSegmentedAddress = 0;
    public int paintingCount = 0;
    public readonly List<uint> textureSegmentedAddresses = [];
    public readonly List<uint> textureSegmentedAddresses_NoPurge = [];
}