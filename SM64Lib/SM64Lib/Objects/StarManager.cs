using global::System.IO;
using System.Linq;
using global::System.Numerics;
using Microsoft.VisualBasic.CompilerServices;
using global::SM64Lib.Data;

namespace SM64Lib.Objects
{
    public class StarPosition
    {
        public Vector3 Position { get; set; }
        public StarNames Name { get; set; }

        public StarPosition()
        {
            Position = Vector3.Zero;
        }

        public StarPosition(StarNames name) : this()
        {
            Name = name;
        }

        public StarPosition(StarNames name, Vector3 position)
        {
            Position = position;
            Name = name;
        }

        public void SavePosition(RomManager rommgr)
        {
            var rom = rommgr.GetBinaryRom(FileAccess.ReadWrite);
            if (!new[] { StarNames.KoopaTheQuick1, StarNames.KoopaTheQuick2 }.Contains(Name))
            {
                WriteStarWrapperFunction(rom);
            }

            var switchExpr = Name;
            switch (switchExpr)
            {
                case StarNames.KoopaTheQuick1:
                    {
                        WritePositionAsShort(rom, Position, StarPositionAddress.KoopaTheQuick1);
                        break;
                    }

                case StarNames.KoopaTheQuick2:
                    {
                        WritePositionAsShort(rom, Position, StarPositionAddress.KoopaTheQuick2);
                        break;
                    }

                case StarNames.KingBobOmbBoss:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.KingBobOmbBoss);
                        rom.Position = 0x62AD4;
                        rom.Write(0x3C048040);   // LUI A0, &H8040
                        rom.Write(0xC1009C0);    // JAL &H80402700
                        rom.Write(0x34844F00);   // ORI A0, A0, &H4F40
                        rom.Write(0x0);
                        rom.Write(0x0);
                        rom.Write(0x0);
                        rom.Write(0x0);
                        rom.Write(0x0);
                        break;
                    }

                case StarNames.WhompBoss:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.WhompBoss);
                        rom.Position = 0x82900;
                        rom.Write(0x3C018040);
                        rom.Write(0xC42C4F10);
                        rom.Write(0xC42E4F14);
                        rom.Write(0x8C264F18);
                        rom.Position = 0x82914;
                        rom.Write(0x0);
                        break;
                    }

                case StarNames.EyerockBoss:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.EyerockBoss);
                        rom.Position = 0xC9A1C;  // 0x8030EA1c
                        rom.Write(0x3C048040);   // LUI A0, &H8040
                        rom.Write(0xC1009C0);    // JAL &H80402700
                        rom.Write(0x34844F20);   // ORI A0, A0, &H4F20
                        rom.Write(0x0);          // NOPs
                        rom.Write(0x0);
                        rom.Write(0x0);
                        break;
                    }

                case StarNames.BigBullyBoss:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.BigBullyBoss);
                        rom.Position = 0xA6970;  // 0x802EB970
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844F30);   // ORI A0, A0, 0x4F30
                        break;
                    }

                case StarNames.ChillBullyBoss:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.ChillBullyBoss);
                        rom.Position = 0xA6950;  // 0x802EB950
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844F40);   // ORI A0, A0, 0x4F40
                        break;
                    }

                case StarNames.GiantPiranhaPlants:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.GiantPiranhaPlants);
                        rom.Position = 0xC802C;  // 0x8030D02C
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844F50);   // ORI A0, A0, 0x4F50
                        break;
                    }

                case StarNames.PenguinMother:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.PenguinMother);
                        rom.Position = 0x7A128;  // 0x802BF128
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844F60);
                        break;
                    }

                case StarNames.BigPenguinRace:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.BigPenguinRace);
                        rom.Position = 0xCD040;  // 0x80312040
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844F90);
                        break;
                    }

                case StarNames.WigglerBoss:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.WigglerBoss);
                        rom.Position = 0xBCFE0;  // 80301FE0
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844F70);
                        break;
                    }

                case StarNames.PeachSlideStar:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.PeachSlideStar);
                        rom.Position = 0xB7D0;   // 80301FE0
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844F80);
                        break;
                    }

                //case StarNames.BigPenguinRace:
                //    {
                //        WritePositionAsSingle(rom, Position, 0x1204F90);
                //        rom.Position = 0x605E4;  // 80301FE0
                //        rom.Write(0x3C048040);   // LUI A0, 0x8040
                //        rom.Write(0xC1009C0);    // JAL 0x80402700
                //        rom.Write(0x34844F90);
                //        break;
                //    }

                case StarNames.TreasureChests:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.TreasureChests);
                        rom.Position = 0xB32B0;
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844FA0);
                        break;
                    }

                case StarNames.BooInHauntedHouse:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.BooInHauntedHouse);
                        rom.Position = 0x7FBB0;
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844FAC);
                        break;
                    }

                case StarNames.Klepto:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.Klepto);
                        rom.Position = 0xCC47C;
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844FC4);
                        break;
                    }

                case StarNames.MerryGoRoundboss:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.MerryGoRoundboss);
                        rom.Position = 0x7FC24;
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844FB8);
                        break;
                    }

                case StarNames.MrIboss:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.MrIboss);
                        rom.Position = 0x61450;
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844FD0);
                        break;
                    }

                case StarNames.RooftopBoo:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.RooftopBoo);
                        rom.Position = 0x7FBEC;
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844FDC);
                        break;
                    }

                case StarNames.SecondactBigBully:
                    {
                        WritePositionAsSingle(rom, Position, StarPositionAddress.SecondactBigBully);
                        rom.Position = 0x7FBEC;
                        rom.Write(0x3C048040);   // LUI A0, 0x8040
                        rom.Write(0xC1009C0);    // JAL 0x80402700
                        rom.Write(0x34844FE4);
                        break;
                    }
            }

            rom.Close();
        }

        private void WritePositionAsShort(BinaryRom rom, Vector3 position, StarPositionAddress offset)
        {
            rom.Position = (int)offset;
            rom.Write(Conversions.ToShort(position.X));
            rom.Write(Conversions.ToShort(position.Y));
            rom.Write(Conversions.ToShort(position.Z));
        }

        private void WritePositionAsSingle(BinaryRom rom, Vector3 position, StarPositionAddress offset)
        {
            rom.Position = (int)offset;
            rom.Write(position.X);
            rom.Write(position.Y);
            rom.Write(position.Z);
        }

        public static void WriteStarWrapperFunction(BinaryRom rom)
        {
            var StarWrapperFunction = new[] { 0x27, 0xBD, 0xFF, 0xE8, 0xAF, 0xBF, 0x0, 0x14, 0xC4, 0x8C, 0x0, 0x0, 0xC4, 0x8E, 0x0, 0x4, 0xC, 0xB, 0xCA, 0xE2, 0x8C, 0x86, 0x0, 0x8, 0x8F, 0xBF, 0x0, 0x14, 0x27, 0xBD, 0x0, 0x18, 0x3, 0xE0, 0x0, 0x8, 0x0, 0x0, 0x0, 0x0 };
            rom.Position = 0x1202700;
            foreach (byte b in StarWrapperFunction)
                rom.Write(b);
        }

        public Vector3 LoadPosition(StarNames name, RomManager romManager)
        {
            var rom = romManager.GetBinaryRom(FileAccess.Read);
            Vector3 starPosition;
            switch(name)
            {
                case StarNames.KoopaTheQuick1:
                    starPosition = ReadPositionAsShort(rom, StarPositionAddress.KoopaTheQuick1);
                    break;
                case StarNames.KoopaTheQuick2:
                    starPosition = ReadPositionAsShort(rom, StarPositionAddress.KoopaTheQuick2);
                    break;
                case StarNames.KingBobOmbBoss:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.KingBobOmbBoss);
                    break;
                case StarNames.WhompBoss:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.WhompBoss);
                    break;
                case StarNames.EyerockBoss:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.EyerockBoss);
                    break;
                case StarNames.BigBullyBoss:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.BigBullyBoss);
                    break;
                case StarNames.ChillBullyBoss:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.ChillBullyBoss);
                    break;
                case StarNames.GiantPiranhaPlants:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.GiantPiranhaPlants);
                    break;
                case StarNames.PenguinMother:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.PenguinMother);
                    break;
                case StarNames.BigPenguinRace:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.BigPenguinRace);
                    break;
                case StarNames.WigglerBoss:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.WigglerBoss);
                    break;
                case StarNames.PeachSlideStar:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.PeachSlideStar);
                    break;
                case StarNames.TreasureChests:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.TreasureChests);
                    break;
                case StarNames.BooInHauntedHouse:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.BooInHauntedHouse);
                    break;
                case StarNames.Klepto:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.Klepto);
                    break;
                case StarNames.MerryGoRoundboss:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.MerryGoRoundboss);
                    break;
                case StarNames.MrIboss:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.MrIboss);
                    break;
                case StarNames.RooftopBoo:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.RooftopBoo);
                    break;
                case StarNames.SecondactBigBully:
                    starPosition = ReadPositionAsSingle(rom, StarPositionAddress.SecondactBigBully);
                    break;
                default:
                    throw new System.ArgumentException(string.Format("{0} is not a valid StarNames value", name));
            }

            rom.Close();
            return starPosition;
        }

        private Vector3 ReadPositionAsShort(BinaryRom rom, StarPositionAddress offset)
        {
            rom.Position = (int)offset;
            int x = rom.ReadInt16();
            int y = rom.ReadInt16();
            int z = rom.ReadInt16();
            return new Vector3(x, y, z);
        }

        private Vector3 ReadPositionAsSingle(BinaryRom rom, StarPositionAddress offset)
        {
            rom.Position = (int)offset;
            float x = rom.ReadSingle();
            float y = rom.ReadSingle();
            float z = rom.ReadSingle();
            return new Vector3(x, y, z);
        }
    }
}