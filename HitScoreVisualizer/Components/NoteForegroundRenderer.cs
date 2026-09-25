using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Zenject;

namespace HitScoreVisualizer.Components;

internal sealed class NoteForegroundRenderer : IInitializable, IDisposable
{
	private const int NoteStencilBit = 128;
	private readonly BeatmapObjectManager beatmapObjectManager;
	private readonly HashSet<Renderer> noteRenderers = new();
	private Material? stencilMaterial;
	private bool loggedFirstNote;

	public NoteForegroundRenderer(BeatmapObjectManager beatmapObjectManager)
	{
		this.beatmapObjectManager = beatmapObjectManager;
	}

	public void Initialize()
	{
		var shader = Shader.Find("UI/Default");
		if (shader == null || !shader.isSupported)
		{
			Plugin.Log.Warn("UI/Default is unavailable; HSV note occlusion is disabled.");
			return;
		}

		stencilMaterial = new Material(shader) { name = "HSV Note Stencil", renderQueue = 4900 };
		stencilMaterial.SetInt("_Stencil", NoteStencilBit);
		stencilMaterial.SetInt("_StencilComp", (int)CompareFunction.Always);
		stencilMaterial.SetInt("_StencilOp", (int)StencilOp.Replace);
		stencilMaterial.SetInt("_StencilReadMask", NoteStencilBit);
		stencilMaterial.SetInt("_StencilWriteMask", NoteStencilBit);
		stencilMaterial.SetInt("_ColorMask", 0);
		stencilMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
		beatmapObjectManager.noteWasSpawnedEvent += NoteWasSpawned;
	}

	private void NoteWasSpawned(NoteController noteController)
	{
		if (stencilMaterial == null)
		{
			return;
		}

		int maskedRenderers = 0;
		foreach (var renderer in noteController.GetComponentsInChildren<Renderer>(true))
		{
			if (renderer is not MeshRenderer and not SkinnedMeshRenderer)
			{
				continue;
			}

			var materials = renderer.sharedMaterials;
			if (materials.Length == 0 || materials[^1] == stencilMaterial ||
				!Array.Exists(materials, material => material != null && material.renderQueue <= 2500))
			{
				continue;
			}

			Array.Resize(ref materials, materials.Length + 1);
			materials[^1] = stencilMaterial;
			renderer.sharedMaterials = materials;
			noteRenderers.Add(renderer);
			maskedRenderers++;
		}

		if (!loggedFirstNote)
		{
			Plugin.Log.Info($"HSV note stencil: {maskedRenderers} opaque renderers on first {noteController.GetType().Name}");
			loggedFirstNote = true;
		}
	}

	public void Dispose()
	{
		beatmapObjectManager.noteWasSpawnedEvent -= NoteWasSpawned;
		foreach (var renderer in noteRenderers)
		{
			if (renderer == null)
			{
				continue;
			}

			var materials = renderer.sharedMaterials;
			if (materials.Length > 0 && materials[^1] == stencilMaterial)
			{
				Array.Resize(ref materials, materials.Length - 1);
				renderer.sharedMaterials = materials;
			}
		}

		noteRenderers.Clear();
		if (stencilMaterial != null)
		{
			UnityEngine.Object.Destroy(stencilMaterial);
		}
	}
}
