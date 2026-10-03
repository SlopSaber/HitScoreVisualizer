using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
	private static BundleReadRequest? preparedBundle;

	public static void PrepareResource()
	{
		if (!shaderLoadAttempted)
		{
			preparedBundle ??= new BundleReadRequest(typeof(HitTextOverlay).Assembly);
		}
	}

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
		var request = preparedBundle ?? new BundleReadRequest(typeof(HitTextOverlay).Assembly);
		preparedBundle = null;
		var bytes = request.Complete();
		if (bytes == null)
		{
			Plugin.Log.Error("HSV no-bloom overlay shader bundle is missing.");
			return null;
		}

		overlayBundle = AssetBundle.LoadFromMemory(bytes);
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

	private sealed class BundleReadRequest
	{
		private readonly Assembly assembly;
		private readonly ManualResetEventSlim physicalReady = new(false);
		private readonly Task<byte[]?> readTask;

		public BundleReadRequest(Assembly assembly)
		{
			this.assembly = assembly;
			if (ExecutionContext.IsFlowSuppressed())
			{
				readTask = ScheduleRead();
			}
			else
			{
				using (ExecutionContext.SuppressFlow())
				{
					readTask = ScheduleRead();
				}
			}
		}

		private Task<byte[]?> ScheduleRead() => Task.Factory.StartNew(Read, CancellationToken.None,
			TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

		public byte[]? Complete()
		{
			// Wait for physical work before observing the task to prevent execution on a cold caller.
			physicalReady.Wait();
			try
			{
				return readTask.GetAwaiter().GetResult();
			}
			finally
			{
				physicalReady.Dispose();
			}
		}

		private byte[]? Read()
		{
			try
			{
				using var stream = assembly.GetManifestResourceStream(BundleResourceName);
				if (stream == null)
				{
					return null;
				}

				using var memory = new MemoryStream();
				stream.CopyTo(memory);
				return memory.ToArray();
			}
			finally
			{
				physicalReady.Set();
			}
		}
	}
}
