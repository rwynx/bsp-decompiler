using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using LibBSP;

namespace Decompiler {
	/// <summary>
	/// Class containing methods for parsing entities from various BSP formats into those for GTKRadiant.
	/// </summary>
	public class EntityToRadiant {

		private Job _master;

		private Entities _entities;
		private MapType _version;

		/// <summary>
		/// Creates a new instance of an <see cref="EntityToGTKRadiant"/> object which will operate on the passed <see cref="Entities"/>.
		/// </summary>
		/// <param name="entities">The <see cref="Entities"/> to postprocess.</param>
		/// <param name="version">The <see cref="MapType"/> of the BSP the entities are from.</param>
		/// <param name="master">The parent <see cref="Job"/> object for this instance.</param>
		public EntityToRadiant(Entities entities, MapType version, Job master) {
			_entities = entities;
			_version = version;
			_master = master;
		}

		/// <summary>
		/// Processes every <see cref="Entity"/> in an <see cref="Entities"/> object to be used in a GTKRadiant map.
		/// </summary>
		public void PostProcessEntities() {
			// There should really only be one of these. But someone might have screwed with the map...
			List<Entity> worldspawns = _entities.FindAll(entity => { return entity.ClassName.Equals("worldspawn", StringComparison.InvariantCultureIgnoreCase); });

			if (_version.IsSubtypeOf(MapType.Quake3)) {
				foreach (Entity worldspawn in worldspawns) {
					foreach (MAPBrush brush in worldspawn.brushes) {
						if (brush.isWater) {
							// Make sure all water brushes currently in the world get converted to Radiant.
							ConvertToWater(brush);
						}
					}
				}
				// Make sure all func_water entities get converted to Radiant.
				List<Entity> waters = _entities.FindAll(entity => { return entity.ClassName.Equals("func_water", StringComparison.InvariantCultureIgnoreCase); });
				if (waters.Any()) {
					// Parse water entities into just water brushes
					foreach (Entity water in waters) {
						ParseWaterIntoWorld(worldspawns[0], water);
						_entities.Remove(water);
					}
				}
			} else if (_version.IsSubtypeOf(MapType.MOHAA)) {
				foreach (Entity worldspawn in worldspawns) {
					foreach (MAPBrush brush in worldspawn.brushes) {
						if (brush.mohTerrain != null) {
							// Convert MoHAA terrain to an EF2 terrain
							brush.ef2Terrain = ConvertToEF2Terrain(brush.mohTerrain);
							brush.mohTerrain = null;
						}
					}
				}
			}

			// We might modify the collection as we iterate over it. Can't use foreach.
			for (int i = 0; i < _entities.Count; ++i) {
				foreach (MAPBrush brush in _entities[i].brushes) {
					if (brush.isDetail) {
						foreach (MAPBrushSide side in brush.sides) {
							side.textureInfo.Flags = side.textureInfo.Flags | 134217728;
						}
					}
				}
				if (!_master.settings.noEntCorrection) {
					PostProcessEntity(_entities[i]);
				}
				if (!_master.settings.noTexCorrection) {
					PostProcessTextures(_entities[i].brushes);
				}
			}
		}

		/// <summary>
		/// Goes through each <see cref="MAPBrush"/> in <paramref name="water"/>, converts it to a water brush for Source,
		/// and adds the <see cref="MAPBrush"/> to <paramref name="world"/>.
		/// </summary>
		/// <param name="world">The world <see cref="Entity"/>.</param>
		/// <param name="water">A water <see cref="Entity"/>.</param>
		private void ParseWaterIntoWorld(Entity world, Entity water) {
			foreach (MAPBrush brush in water.brushes) {
				ConvertToWater(brush);
				world.brushes.Add(brush);
			}
		}

		/// <summary>
		/// For <paramref name="brush"/>, sets the top <see cref="MAPBrushSide"/>'s texture
		/// to a water texture and sets all others to nodraw.
		/// </summary>
		/// <param name="brush">The <see cref="MAPBrush"/> to make into a water brush.</param>
		private void ConvertToWater(MAPBrush brush) {
			foreach (MAPBrushSide side in brush.sides) {
				side.texture = "Shaders/liquids/clear_calm1";
			}
		}

		/// <summary>
		/// Converts the passed <see cref="MAPTerrainMoHAA"/> into a <see cref="MAPTerrainEF2"/>
		/// with the same heightmap.
		/// </summary>
		/// <param name="mohTerrain">The <see cref="MAPTerrainMoHAA"/> to convert.</param>
		/// <returns><see cref="MAPTerrainEF2"/> with the same shader and heightmap as <paramref name="mohTerrain"/>.</returns>
		private MAPTerrainEF2 ConvertToEF2Terrain(MAPTerrainMoHAA mohTerrain) {
			if (mohTerrain.size == new Vector2(9, 9)) {
				MAPTerrainMoHAA.Partition partition = mohTerrain.partitions[0];
				MAPTerrainEF2 ef2Terrain = new MAPTerrainEF2() {
					side = 9,
					texture = partition.shader,
					textureShiftS = partition.textureShift[0],
					textureShiftT = partition.textureShift[1],
					texRot = (float)partition.rotation,
					texScaleX = partition.textureScale[0],
					texScaleY = partition.textureScale[1],
					flags = 1024, // maybe don't hardcode this?
					sideLength = 512,
					start = new Vector3(mohTerrain.origin.X, mohTerrain.origin.Y, 0),
					IF = Vector4.Zero,
					LF = Vector4.Zero,
					heightMap = new float[9, 9],
					alphaMap = new float[9, 9]
				};
				for (int y = 0; y < ef2Terrain.heightMap.GetLength(0); ++y) {
					for (int x = 0; x < ef2Terrain.heightMap.GetLength(1); ++x) {
						ef2Terrain.heightMap[y, x] = mohTerrain.vertices[(y * (int)mohTerrain.size.Y) + x].height + (float)mohTerrain.origin.Z;
					}
				}
				return ef2Terrain;
			}
			return null;
		}

		/// <summary>
		/// Sends <paramref name="entity"/> to be postprocessed into the appropriate method based on version.
		/// </summary>
		/// <param name="entity"><see cref="Entity"/> to postprocess.</param>
		private void PostProcessEntity(Entity entity) {
			if (entity.IsBrushBased) {
				Vector3 origin = entity.Origin;
				entity.Remove("origin");
				entity.Remove("model");
				if (origin != Vector3.Zero) {
					// If this brush has an origin
					MAPBrush neworiginBrush = MAPBrushExtensions.CreateCube(new Vector3(-16, -16, -16), new Vector3(16, 16, 16), "common/origin");
					entity.brushes.Add(neworiginBrush);
				}
				foreach (MAPBrush brush in entity.brushes) {
					brush.Translate(origin);
				}
			}

			if (_version == MapType.Nightfire) {
				PostProcessNightfireEntity(entity);
			}
		}

		/// <summary>
		/// Postprocesser to convert an <see cref="Entity"/> from a Nightfire BSP to one for GTKRadiant.
		/// </summary>
		/// <param name="entity">The <see cref="Entity"/> to parse.</param>
		private void PostProcessNightfireEntity(Entity entity) {
			switch (entity.ClassName.ToLower()) {
				case "func_door_rotating": {
					entity["classname"] = "func_rotatingdoor";
					break;
				}
				case "worldspawn": {
					entity.Remove("mapversion");
					break;
				}
			}
		}

		/// <summary>
		/// Every <see cref="MAPBrushSide"/> contained in <paramref name="brushes"/> will have its texture examined,
		/// and, if necessary, replaced with the equivalent for GTKRadiant.
		/// </summary>
		/// <param name="brushes">The collection of <see cref="MAPBrush"/> objects to have textures parsed.</param>
		/// <param name="version">The <see cref="MapType"/> of the BSP this entity came from.</param>
		private void PostProcessTextures(IEnumerable<MAPBrush> brushes) {
			foreach (MAPBrush brush in brushes) {
				foreach (MAPBrushSide brushSide in brush.sides) {
					brushSide.textureInfo.Validate(brushSide.plane);
					PostProcessSpecialTexture(brushSide);
					if (_version == MapType.Nightfire) {
						PostProcessNightfireTexture(brushSide);
					} else if (_version.IsSubtypeOf(MapType.Source)) {
						PostProcessSourceTexture(brushSide);
					} else if (_version.IsSubtypeOf(MapType.Quake3)) {
						PostProcessQuake3Texture(brushSide);
					}
				}
				if (brush.patch != null) {
					PostProcessQuake3Texture(brush.patch);
				}
				if (brush.ef2Terrain != null) {
					PostProcessQuake3Texture(brush.ef2Terrain);
				}
			}
		}

		/// <summary>
		/// Postprocesser to convert the texture referenced by <paramref name="brushSide"/> into one used by GTKRadiant, if necessary.
		/// These textures are produced by the decompiler algorithm itself.
		/// </summary>
		/// <param name="brushSide">The <see cref="MAPBrushSide"/> to have its texture parsed.</param>
		private void PostProcessSpecialTexture(MAPBrushSide brushSide) {
			switch (brushSide.texture.ToLower()) {
				case "**nulltexture**":
				case "**nodrawtexture**": {
					brushSide.texture = "common/nodraw";
					break;
				}
				case "**skiptexture**": {
					brushSide.texture = "common/skip";
					break;
				}
				case "**skytexture**": {
					brushSide.texture = "common/skyportal";
					break;
				}
				case "**hinttexture**": {
					brushSide.texture = "common/hint";
					break;
				}
				case "**cliptexture**": {
					brushSide.texture = "common/clip";
					break;
				}
			}
		}

		/// <summary>
		/// Postprocesser to convert the texture referenced by <paramref name="brushSide"/> into one used by GTKRadiant, if necessary.
		/// </summary>
		/// <param name="brushSide">The <see cref="MAPBrushSide"/> to have its texture parsed.</param>
		private void PostProcessNightfireTexture(MAPBrushSide brushSide) {
			switch (brushSide.texture.ToLower()) {
				case "special/nodraw":
				case "special/null": {
					brushSide.texture = "common/nodraw";
					break;
				}
				case "special/clip": {
					brushSide.texture = "common/clip";
					break;
				}
				case "special/sky": {
					brushSide.texture = "common/skyportal";
					break;
				}
				case "special/trigger": {
					brushSide.texture = "common/trigger";
					break;
				}
				case "special/playerclip": {
					brushSide.texture = "common/playerclip";
					break;
				}
				case "special/hint": {
					brushSide.texture = "common/hint";
					break;
				}
				case "special/skip": {
					brushSide.texture = "common/skip";
					break;
				}
				case "special/npcclip":
				case "special/enemyclip": {
					brushSide.texture = "common/tankclip";
					break;
				}
			}
		}

		/// <summary>
		/// Postprocesser to convert the texture referenced by <paramref name="brushSide"/> into one used by GTKRadiant, if necessary.
		/// </summary>
		/// <param name="brushSide">The <see cref="MAPBrushSide"/> to have its texture parsed.</param>
		private void PostProcessQuake3Texture(MAPBrushSide brushSide) {
			if (brushSide.texture.Length >= 9 && brushSide.texture.Substring(0, 9).Equals("textures/", StringComparison.InvariantCultureIgnoreCase)) {
				brushSide.texture = brushSide.texture.Substring(9);
			}
		}

		/// <summary>
		/// Postprocesser to convert the texture referenced by <paramref name="patch"/> into one used by GTKRadiant, if necessary.
		/// </summary>
		/// <param name="patch">The <see cref="MAPPatch"/> to have its texture parsed.</param>
		private void PostProcessQuake3Texture(MAPPatch patch) {
			if (patch.texture.Length >= 9 && patch.texture.Substring(0, 9).Equals("textures/", StringComparison.InvariantCultureIgnoreCase)) {
				patch.texture = patch.texture.Substring(9);
			}
		}

		/// <summary>
		/// Postprocesser to convert the texture referenced by <paramref name="terrain"/> into one used by GTKRadiant, if necessary.
		/// </summary>
		/// <param name="terrain">The <see cref="MAPTerrainEF2"/> to have its texture parsed.</param>
		private void PostProcessQuake3Texture(MAPTerrainEF2 terrain) {
			if (terrain.texture.Length >= 9 && terrain.texture.Substring(0, 9).Equals("textures/", StringComparison.InvariantCultureIgnoreCase)) {
				terrain.texture = terrain.texture.Substring(9);
			}
		}

		/// <summary>
		/// Postprocesser to convert the texture referenced by <paramref name="brushSide"/> into one used by GTKRadiant, if necessary.
		/// </summary>
		/// <param name="brushSide">The <see cref="MAPBrushSide"/> to have its texture parsed.</param>
		private void PostProcessSourceTexture(MAPBrushSide brushSide) {
			if (brushSide.texture.Length >= 5 && brushSide.texture.Substring(0, 5).Equals("maps/", StringComparison.InvariantCultureIgnoreCase)) {
				brushSide.texture = brushSide.texture.Substring(5);
				for (int i = 0; i < brushSide.texture.Length; ++i) {
					if (brushSide.texture[i] == '/') {
						brushSide.texture = brushSide.texture.Substring(i + 1);
						break;
					}
				}
			}

			switch (brushSide.texture.ToLower()) {
				case "tools/toolshint": {
					brushSide.texture = "common/hint";
					break;
				}
				case "tools/toolsskip": {
					brushSide.texture = "common/skip";
					break;
				}
				case "tools/toolsinvisible":
				case "tools/toolsplayerclip":
				case "tools/toolsclip": {
					brushSide.texture = "common/clip";
					break;
				}
				case "tools/toolstrigger":
				case "tools/toolsfog": {
					brushSide.texture = "common/trigger";
					break;
				}
				case "tools/toolsskybox": {
					brushSide.texture = "common/skyportal";
					break;
				}
				case "tools/toolsnodraw": {
					brushSide.texture = "common/nodraw";
					break;
				}
				case "tools/toolsnpcclip": {
					brushSide.texture = "common/tankclip";
					break;
				}
			}

			// Parse cubemap textures
			// I'm sure this could be done more concisely with regex, but I suck at regex.
			int numUnderscores = 0;
			bool validnumber = false;
			for (int i = brushSide.texture.Length - 1; i > 0; --i) {
				if (brushSide.texture[i] <= '9' && brushSide.texture[i] >= '0') {
					// Current is a number, this may be a cubemap reference
					validnumber = true;
				} else {
					if (brushSide.texture[i] == '-') {
						// Current is a minus sign (-).
						if (!validnumber) {
							break; // Make sure there's a number to add the minus sign to. If not, kill the loop.
						}
					} else {
						if (brushSide.texture[i] == '_') {
							// Current is an underscore (_)
							if (validnumber) {
								// Make sure there is a number in the current string
								++numUnderscores; // before moving on to the next one.
								if (numUnderscores == 3) {
									// If we've got all our numbers
									brushSide.texture = brushSide.texture.Substring(0, i); // Cut the texture string
									break; // Kill the loop, we're done
								}
								validnumber = false;
							} else {
								// No number after the underscore
								break;
							}
						} else {
							// Not an acceptable character
							break;
						}
					}
				}
			}

		}

	}
}
