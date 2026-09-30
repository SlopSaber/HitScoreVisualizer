using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Zenject;

namespace HitScoreVisualizer.Components;

internal sealed class NoteForegroundRenderer : IInitializable, ILateTickable, IDisposable
{
	private const int NoteStencilBit = 128;
	private static readonly int CutoutPropertyId = Shader.PropertyToID("_Cutout");
	private readonly BeatmapObjectManager beatmapObjectManager;
	private readonly HashSet<Renderer> noteRenderers = new();
	private readonly Dictionary<NoteController, List<Renderer>> activeNotes = new();
	private readonly Dictionary<Type, PropertyInfo?> customDataProperties = new();
	private readonly MaterialPropertyBlock propertyBlock = new();
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
		beatmapObjectManager.noteWasDespawnedEvent += NoteWasDespawned;
	}

	private void NoteWasSpawned(NoteController noteController)
	{
		if (stencilMaterial == null)
		{
			return;
		}

		// Controllers are pooled between playable and decorative notes.
		NoteWasDespawned(noteController);
		if (IsFakeNote(noteController.noteData))
		{
			return;
		}

		var renderers = new List<Renderer>();
		int maskedRenderers = 0;
		foreach (var renderer in noteController.GetComponentsInChildren<Renderer>(true))
		{
			if (renderer is not MeshRenderer and not SkinnedMeshRenderer)
			{
				continue;
			}

			var materials = renderer.sharedMaterials;
			if (materials.Length == 0 ||
				!Array.Exists(materials, material => material != null && material.renderQueue <= 2500))
			{
				continue;
			}

			renderers.Add(renderer);
			UpdateMask(renderer);
			maskedRenderers++;
		}
		activeNotes.Add(noteController, renderers);

		if (!loggedFirstNote)
		{
			Plugin.Log.Info($"HSV note stencil: {maskedRenderers} opaque renderers on first {noteController.GetType().Name}");
			loggedFirstNote = true;
		}
	}

	private bool IsFakeNote(NoteData noteData)
	{
		var type = noteData.GetType();
		if (!customDataProperties.TryGetValue(type, out var property))
		{
			property = type.GetProperty("customData");
			customDataProperties.Add(type, property);
		}

		// Keep CustomJSONData optional; v3 fake arrays carry Noodle's NE_fake marker.
		return property?.GetValue(noteData) is IDictionary<string, object> customData &&
			(customData.TryGetValue("_fake", out var fake) && fake is true ||
			 customData.TryGetValue("NE_fake", out var internalFake) && internalFake is true);
	}

	public void LateTick()
	{
		foreach (var renderers in activeNotes.Values)
		{
			foreach (var renderer in renderers)
			{
				UpdateMask(renderer);
			}
		}
	}

	private void UpdateMask(Renderer renderer)
	{
		if (renderer == null)
		{
			return;
		}

		propertyBlock.Clear();
		renderer.GetPropertyBlock(propertyBlock);
		// UI/Default cannot reproduce the note shader's dissolve. Only fully visible
		// note bodies may write a full-mesh silhouette into the score stencil.
		SetMask(renderer, propertyBlock.GetFloat(CutoutPropertyId) <= 0f);
	}

	private void SetMask(Renderer renderer, bool enabled)
	{
		if (renderer == null || enabled == noteRenderers.Contains(renderer))
		{
			return;
		}

		var materials = renderer.sharedMaterials;
		if (enabled)
		{
			Array.Resize(ref materials, materials.Length + 1);
			materials[^1] = stencilMaterial!;
			noteRenderers.Add(renderer);
		}
		else
		{
			var index = Array.IndexOf(materials, stencilMaterial);
			if (index >= 0)
			{
				Array.Copy(materials, index + 1, materials, index, materials.Length - index - 1);
				Array.Resize(ref materials, materials.Length - 1);
			}
			noteRenderers.Remove(renderer);
		}
		renderer.sharedMaterials = materials;
	}

	private void NoteWasDespawned(NoteController noteController)
	{
		if (!activeNotes.TryGetValue(noteController, out var renderers))
		{
			return;
		}
		foreach (var renderer in renderers)
		{
			SetMask(renderer, false);
		}
		activeNotes.Remove(noteController);
	}

	public void Dispose()
	{
		beatmapObjectManager.noteWasSpawnedEvent -= NoteWasSpawned;
		beatmapObjectManager.noteWasDespawnedEvent -= NoteWasDespawned;
		foreach (var renderers in activeNotes.Values)
		{
			foreach (var renderer in renderers)
			{
				SetMask(renderer, false);
			}
		}

		activeNotes.Clear();
		noteRenderers.Clear();
		if (stencilMaterial != null)
		{
			UnityEngine.Object.Destroy(stencilMaterial);
		}
	}
}
