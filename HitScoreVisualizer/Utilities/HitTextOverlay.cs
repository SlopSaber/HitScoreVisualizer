using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HitScoreVisualizer.Utilities;

internal static class HitTextOverlay
{
	private const string BundleResourceName = "HitScoreVisualizer.Shaders.hsv-text-overlay.bundle";
	private static AssetBundle? overlayBundle;
	private static Shader? overlayShader;
	private static bool shaderLoadAttempted;
	private static bool shaderChoiceLogged;

	public static void Configure(TextMeshPro text)
	{
		// fontMaterial gives this text its own material, leaving menu previews and other mods alone.
		var material = text.fontMaterial;
		var shader = GetOverlayShader();
		if (shader == null)
		{
			return;
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

	private static Shader? GetOverlayShader()
	{
		if (shaderLoadAttempted)
		{
			return overlayShader;
		}

		shaderLoadAttempted = true;
		using var stream = typeof(HitTextOverlay).Assembly.GetManifestResourceStream(BundleResourceName);
		if (stream == null)
		{
			Plugin.Log.Error("HSV no-bloom overlay shader bundle is missing.");
			return null;
		}

		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		overlayBundle = AssetBundle.LoadFromMemory(memory.ToArray());
		if (overlayBundle == null)
		{
			Plugin.Log.Error("HSV no-bloom overlay shader bundle could not be opened.");
			return null;
		}

		var shaders = overlayBundle.LoadAllAssets<Shader>();
		overlayShader = shaders is { Length: > 0 } ? shaders[0] : null;
		if (overlayShader == null)
		{
			Plugin.Log.Error("HSV no-bloom overlay shader is missing from the bundle.");
			return null;
		}
		if (!overlayShader.isSupported)
		{
			Plugin.Log.Error("HSV no-bloom overlay shader is unsupported by this renderer.");
			return null;
		}

		return overlayShader;
	}
}
