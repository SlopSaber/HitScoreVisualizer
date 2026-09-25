using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace HitScoreVisualizer.Components;

internal sealed class NoteForegroundRenderer : IInitializable, IDisposable
{
	private sealed record OriginalRendererState(Material[] Materials, Material[] OverlayMaterials,
		int SortingLayer, int SortingOrder);

	private readonly BeatmapObjectManager beatmapObjectManager;
	private readonly Dictionary<Renderer, OriginalRendererState> renderers = new();
	private readonly List<Material> ownedMaterials = new();

	public NoteForegroundRenderer(BeatmapObjectManager beatmapObjectManager)
	{
		this.beatmapObjectManager = beatmapObjectManager;
	}

	public void Initialize()
	{
		beatmapObjectManager.noteWasSpawnedEvent += NoteWasSpawned;
	}

	private void NoteWasSpawned(NoteController noteController)
	{
		foreach (var renderer in noteController.GetComponentsInChildren<Renderer>(true))
		{
			if (renderer is not MeshRenderer and not SkinnedMeshRenderer)
			{
				continue;
			}

			renderers.TryGetValue(renderer, out var previous);
			if (previous != null)
			{
				if (UsesMaterials(renderer, previous.OverlayMaterials))
				{
					continue;
				}
			}

			var originals = renderer.sharedMaterials;
			var overlays = new Material[originals.Length];
			for (int i = 0; i < originals.Length; i++)
			{
				if (originals[i] != null)
				{
					overlays[i] = new Material(originals[i]) { renderQueue = 5000 };
					ownedMaterials.Add(overlays[i]);
				}
			}

			renderers[renderer] = new OriginalRendererState(originals, overlays,
				previous?.SortingLayer ?? renderer.sortingLayerID,
				previous?.SortingOrder ?? renderer.sortingOrder);
			renderer.sharedMaterials = overlays;
			renderer.sortingLayerID = 0;
			renderer.sortingOrder = short.MaxValue;
		}
	}

	public void Dispose()
	{
		beatmapObjectManager.noteWasSpawnedEvent -= NoteWasSpawned;
		foreach (var (renderer, state) in renderers)
		{
			if (renderer != null && UsesMaterials(renderer, state.OverlayMaterials))
			{
				renderer.sharedMaterials = state.Materials;
				renderer.sortingLayerID = state.SortingLayer;
				renderer.sortingOrder = state.SortingOrder;
			}

		}

		foreach (var material in ownedMaterials)
		{
			if (material != null)
			{
				UnityEngine.Object.Destroy(material);
			}
		}

		ownedMaterials.Clear();
		renderers.Clear();
	}

	private static bool UsesMaterials(Renderer renderer, Material[] materials)
	{
		var current = renderer.sharedMaterials;
		if (current.Length != materials.Length)
		{
			return false;
		}

		for (int i = 0; i < current.Length; i++)
		{
			if (current[i] != materials[i])
			{
				return false;
			}
		}

		return true;
	}

}
