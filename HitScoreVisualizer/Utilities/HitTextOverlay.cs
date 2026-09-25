using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HitScoreVisualizer.Utilities;

internal static class HitTextOverlay
{
	public static void Configure(TextMeshPro text)
	{
		// fontMaterial gives this text its own material, leaving menu previews and other mods alone.
		var material = text.fontMaterial;
		var overlayName = material.shader.name switch
		{
			"TextMeshPro/Distance Field" => "TextMeshPro/Distance Field Overlay",
			"TextMeshPro/Mobile/Distance Field" => "TextMeshPro/Mobile/Distance Field Overlay",
			_ => null,
		};
		if (overlayName != null)
		{
			var overlayShader = Shader.Find(overlayName);
			if (overlayShader != null && overlayShader.isSupported)
			{
				material.shader = overlayShader;
			}
		}

		material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
		material.SetInt("_ZTestMode", (int)CompareFunction.Always);
		material.renderQueue = 4999;
		text.renderer.sortingLayerID = 0;
		text.renderer.sortingOrder = short.MaxValue - 1;
	}
}
