using System;
using SM64Lib;
using SM64Lib.Levels;
using Z.Core.Extensions;

Console.WriteLine("hmm");
RomManager manger = new("b3313 silved.z64");
Console.WriteLine("a manger?");
manger.LoadRom();
RomSpaceInfo info = manger.GetRomSpaceInfo();
Console.WriteLine($"ROM size pre compression: {((0x1210000 + info.TotalUsedSpace) / 1024) / 1024.0} MiB");

Console.WriteLine($"loadmad {manger.Levels.Count} levels");
foreach (Level level in manger.Levels) {
    Console.WriteLine(level.LevelID);
    foreach (LevelArea area in level.Areas) {
        Console.WriteLine($"\t{area.AreaID}: {area.Bank0xELength.ToString("X2")}");
    }
}

manger.SaveRom(true, true, RecalcChecksumBehavior.Always);
info = manger.GetRomSpaceInfo();
Console.WriteLine($"ROM size post compression: {((0x1210000 + info.TotalUsedSpace) / 1024) / 1024.0} MiB");
