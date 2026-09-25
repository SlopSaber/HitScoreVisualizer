using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildHsvTextOverlay
{
	public static void Build()
	{
		const string assetPath = "Assets/Shaders/HsvTextOverlay.shader";
		const string bundleName = "hsv-text-overlay.bundle";
		var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
		if (shader == null || ShaderUtil.ShaderHasError(shader))
		{
			throw new InvalidOperationException("HSV overlay shader did not compile.");
		}

		var output = Path.GetFullPath("Output");
		Directory.CreateDirectory(output);
		var bundles = new[]
		{
			new AssetBundleBuild
			{
				assetBundleName = bundleName,
				assetNames = new[] { assetPath },
			},
		};
		var manifest = BuildPipeline.BuildAssetBundles(output, bundles,
			BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
		if (manifest == null || !File.Exists(Path.Combine(output, bundleName)))
		{
			throw new InvalidOperationException("HSV overlay bundle was not produced.");
		}
	}
}
