using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HitScoreVisualizer.Utilities;

internal static class HitTextOverlay
{
	private const string OverlayShaderName = "TextMeshPro/Distance Field Overlay";
	private static Shader? overlayShader;
	private static bool missingShaderLogged;
	private static bool shaderChoiceLogged;

	public static void Configure(TextMeshPro text)
	{
		// fontMaterial gives this text its own material, leaving menu previews and other mods alone.
		var material = text.fontMaterial;
		var shader = overlayShader;
		if (shader == null)
		{
			shader = Shader.Find(OverlayShaderName) ?? Resources.FindObjectsOfTypeAll<Shader>()
				.FirstOrDefault(candidate => candidate.name == OverlayShaderName);
			if (shader == null || !shader.isSupported)
			{
				if (!missingShaderLogged)
				{
					Plugin.Log.Warn("TMP overlay shader is unavailable; scenery can still occlude HSV text.");
					missingShaderLogged = true;
				}

				return;
			}

			overlayShader = shader;
		}

		if (!shaderChoiceLogged)
		{
			Plugin.Log.Info($"HSV text shader: {material.shader.name} -> {shader.name}");
			shaderChoiceLogged = true;
		}

		material.shader = shader;
		material.SetInt("_Stencil", 128);
		material.SetInt("_StencilComp", (int)CompareFunction.NotEqual);
		material.SetInt("_StencilReadMask", 128);
		material.renderQueue = 4999;
		text.renderer.sortingLayerID = 0;
		text.renderer.sortingOrder = short.MaxValue - 1;
	}
}
