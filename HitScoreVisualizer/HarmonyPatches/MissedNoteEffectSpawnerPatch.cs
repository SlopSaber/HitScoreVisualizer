using System;
using System.Runtime.Remoting.Messaging;
using HitScoreVisualizer.Components;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities;
using SiraUtil.Affinity;

namespace HitScoreVisualizer.HarmonyPatches;

internal class MissedNoteEffectSpawnerPatch : IAffinity
{
	private readonly HsvFlyingEffectSpawner flyingEffectSpawner;
	private readonly HsvConfigModel config;
	private readonly PlayerMovementFollower playerMovementFollower;
	private readonly Random random = new();

	private readonly ArrayPicker<MissDisplay> missPicker = new([]);

	public MissedNoteEffectSpawnerPatch(HsvFlyingEffectSpawner flyingEffectSpawner, HsvConfigModel config, PlayerMovementFollower playerMovementFollower)
	{
		this.flyingEffectSpawner = flyingEffectSpawner;
		this.config = config;
		this.playerMovementFollower = playerMovementFollower;

		if (config.MissDisplays is null)
		{
			return;
		}

		missPicker = new(config.MissDisplays.ToArray());
	}

	[AffinityPrefix]
	[AffinityPriority(1000)]
	[AffinityPatch(typeof(MissedNoteEffectSpawner), nameof(MissedNoteEffectSpawner.HandleNoteWasMissed))]
	private bool HandleNoteWasMissedPrefix(MissedNoteEffectSpawner __instance, NoteController noteController)
	{
		if (noteController.hidden
		    || noteController.noteData.time + 0.5f < __instance._audioTimeSyncController.songTime
		    || noteController.noteData.colorType == ColorType.None)
		{
			// Do nothing
			return false;
		}

		return !TrySpawnText(missPicker, noteController, __instance._spawnPosZ);
	}

	private bool TrySpawnText(ArrayPicker<MissDisplay> picker, NoteController noteController, float spawnPosZ)
	{
		if (config.RandomizeMissDisplays && picker.TryGetRandom(random, out var display))
		{
			SpawnText(display, noteController, spawnPosZ);
			return true;
		}

		if (picker.TryGetNext(out display))
		{
			SpawnText(display, noteController, spawnPosZ);
			return true;
		}

		return false;
	}

	private void SpawnText(MissDisplay display, NoteController noteController, float spawnPosZ)
	{
		if (!playerMovementFollower.HasPlayerTrack)
		{
			var originalPosition = noteController.inverseWorldRotation * noteController.noteTransform.position;
			originalPosition.z = spawnPosZ;
			flyingEffectSpawner.SpawnText(
				noteController.worldRotation * originalPosition,
				noteController.worldRotation,
				noteController.inverseWorldRotation,
				display.Text,
				display.Color);
			return;
		}

		var originPosition = playerMovementFollower.PlayerOriginPosition;
		var position = noteController.inverseWorldRotation * (noteController.noteTransform.position - originPosition);
		position.z = spawnPosZ;

		flyingEffectSpawner.SpawnText(
			originPosition + noteController.worldRotation * position,
			noteController.worldRotation,
			noteController.inverseWorldRotation,
			display.Text,
			display.Color);
	}
}
