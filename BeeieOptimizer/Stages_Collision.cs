using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SM64Lib;
using SM64Lib.Levels;
using SM64Lib.Model.Collision;

using static Simplicity;

static partial class Stages {
	public static void OptimizeCollision(RomManager manger, Config config) {
		StreamWriter collisionData = new("collisionData.txt");
		foreach (Level level in manger.Levels) {
			foreach (LevelArea area in level.Areas) {
				if (area.AreaModel.Collision.Mesh.Triangles.Count == 0) continue;
				if (config.CollisionIgnoreAreas.Any(ptn => ptn.Match(manger, level, area))) {
					Console.WriteLine("Collisione: Ignoring {level.LevelID} {area.AreaID}");
					continue;
				}
				collisionData.WriteLine($"AREA {level.LevelID} {area.AreaID}");

				/*if (level.LevelID == 0x6 && area.AreaID == 11) {
				*               Console.WriteLine(string.Join(";", (IEnumerable<Geopointer>)area.AreaModel.Fast3DBuffer.DLPointers));
				*               var bin = new SM64Lib.Data.BinaryFile("dump.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite) { Position = 0 };
				*               area.AreaModel.ToBinaryData(bin, 0, 0, 0xE000000, manger.RomConfig.CollisionBaseConfig);
				*               bin.Close();
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

		string blendball = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blendmaballs.py");
		Process p = Process.Start("blender", $"-b --python \"{blendball}\"");
		p.WaitForExit();

		rm_f("collisionData.txt");
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

		rm_f("collisionData.txt");
	}


	public static void EvaporateCollision(RomManager manger) {
		foreach (Level level in manger.Levels) {
			foreach (LevelArea area in level.Areas) {
				area.AreaModel.Collision.Mesh.Vertices = [];
				area.AreaModel.Collision.Mesh.Triangles = [];
			}
		}
	}
}
