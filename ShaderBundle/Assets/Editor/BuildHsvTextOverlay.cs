using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildHsvTextOverlay
{
	public static void Build()
	{
		const string assetPath = "Assets/Shaders/HsvTextOverlay.shader";
		const string materialPath = "Assets/Shaders/HsvTextOverlay.mat";
		const string bundleName = "hsv-text-overlay.bundle";
		var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
		if (shader == null || ShaderUtil.ShaderHasError(shader))
		{
			throw new InvalidOperationException("HSV overlay shader did not compile.");
		}
		var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
		if (material == null)
		{
			material = new Material(shader);
			AssetDatabase.CreateAsset(material, materialPath);
		}
		else
		{
			material.shader = shader;
			EditorUtility.SetDirty(material);
		}
		AssetDatabase.SaveAssets();

		var output = Path.GetFullPath("Output");
		Directory.CreateDirectory(output);
		var bundles = new[]
		{
			new AssetBundleBuild
			{
				assetBundleName = bundleName,
				assetNames = new[] { materialPath, assetPath },
			},
		};
		var parameters = new BuildAssetBundlesParameters
		{
			outputPath = output,
			bundleDefinitions = bundles,
			options = BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
			targetPlatform = BuildTarget.StandaloneWindows64,
		};
		var manifest = BuildPipeline.BuildAssetBundles(parameters);
		if (manifest == null || !File.Exists(Path.Combine(output, bundleName)))
		{
			throw new InvalidOperationException("HSV overlay bundle was not produced.");
		}
		var builtBundle = AssetBundle.LoadFromFile(Path.Combine(output, bundleName));
		if (builtBundle == null)
		{
			throw new InvalidOperationException("HSV overlay bundle could not be opened after build.");
		}
		try
		{
			if (builtBundle.LoadAsset<Shader>(assetPath) == null)
			{
				throw new InvalidOperationException("HSV overlay shader is missing from the built bundle.");
			}
		}
		finally
		{
			builtBundle.Unload(false);
		}
	}
}
