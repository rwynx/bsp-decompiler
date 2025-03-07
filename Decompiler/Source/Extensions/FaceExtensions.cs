using System;
using System.Collections.Generic;
using System.Numerics;

using LibBSP;

namespace Decompiler {
	/// <summary>
	/// Static class containing helper functions for working with <see cref="Face"/> objects.
	/// </summary>
	public static class FaceExtensions {

		/// <summary>
		/// Creates a new <see cref="MAPBrush"/> object using this <see cref="Face"/>. The brush will simply have the
		/// face as its front, the edges will be extruded by <paramref name="depth"/> and will be textured with the
		/// "nodraw" texture, as well as the back.
		/// </summary>
		/// <param name="face">This <see cref="Face"/>.</param>
		/// <param name="bsp">The <see cref="BSP"/> object this <see cref="Face"/> is from.</param>
		/// <param name="depth">The desired depth of the resulting brush.</param>
		/// <returns>A <see cref="MAPBrush"/> object representing the passed <paramref name="face"/>.</returns>
		public static MAPBrush CreateBrush(this Face face, BSP bsp, float depth) {
			TextureInfo texInfo;
			string texture;
			if (face.TextureInfoIndex >= 0) {
				texInfo = bsp.TextureInfo[face.TextureInfoIndex];
				if (bsp.TextureData != null) {
					TextureData texData = bsp.TextureData[texInfo.TextureIndex];
					texture = bsp.Textures.GetTextureAtOffset((uint)bsp.TextureTable[texData.TextureStringOffsetIndex]);
				} else {
					Texture texData = bsp.Textures[texInfo.TextureIndex];
					texture = texData.Name;
				}
			} else {
				Vector3[] axes = TextureInfo.TextureAxisFromPlane(bsp.Planes[face.PlaneIndex]);
				texInfo = new TextureInfo(axes[0], axes[1], Vector2.Zero, Vector2.One, 0, -1, 0);
				texture = "**cliptexture**";
			}
			
			TextureInfo outputTexInfo = texInfo.BSP2MAPTexInfo(Vector3.Zero);

			// Turn vertices and edges into arrays of vectors
			Vector3[] froms = new Vector3[face.NumEdgeIndices];
			Vector3[] tos = new Vector3[face.NumEdgeIndices];
			for (int i = 0; i < face.NumEdgeIndices; ++i) {
				if (bsp.FaceEdges[face.FirstEdgeIndexIndex + i] > 0) {
					froms[i] = bsp.Vertices[bsp.Edges[(int)bsp.FaceEdges[face.FirstEdgeIndexIndex + i]].FirstVertexIndex].position;
					tos[i] = bsp.Vertices[bsp.Edges[(int)bsp.FaceEdges[face.FirstEdgeIndexIndex + i]].SecondVertexIndex].position;
				} else {
					tos[i] = bsp.Vertices[bsp.Edges[(int)bsp.FaceEdges[face.FirstEdgeIndexIndex + i] * (-1)].FirstVertexIndex].position;
					froms[i] = bsp.Vertices[bsp.Edges[(int)bsp.FaceEdges[face.FirstEdgeIndexIndex + i] * (-1)].SecondVertexIndex].position;
				}
			}

			return MAPBrushExtensions.CreateBrushFromWind(froms, tos, texture, "**nodrawtexture**", outputTexInfo, depth);
		}

	}
}
