using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace HitScoreVisualizer.Components;

internal class PlayerMovementFollower : IInitializable, ILateTickable, IDisposable
{
	private const float LaneTransitionSeconds = 0.25f;

	private struct LaneRotation
	{
		public float Time;
		public Quaternion Rotation;
		public float TransitionStartTime;
		public Quaternion TransitionStartRotation;
	}

	private readonly PlayerTransforms playerTransforms;
	private readonly BeatmapObjectManager beatmapObjectManager;
	private readonly AudioTimeSyncController audioTimeSyncController;
	private readonly bool trackLaneRotation;
	private readonly List<LaneRotation> laneRotations = new();
	private Transform? laneOrigin;

	public PlayerMovementFollower(PlayerTransforms playerTransforms, BeatmapObjectManager beatmapObjectManager,
		AudioTimeSyncController audioTimeSyncController, GameplayCoreSceneSetupData sceneData)
	{
		this.playerTransforms = playerTransforms;
		this.beatmapObjectManager = beatmapObjectManager;
		this.audioTimeSyncController = audioTimeSyncController;
		trackLaneRotation = sceneData.beatmapKey.characteristic != BeatmapCharacteristic.Degree90 &&
		                    sceneData.beatmapKey.characteristic != BeatmapCharacteristic.Degree360;
	}

	private Transform? PlayerOrigin => playerTransforms._originTransform.parent;
	private Transform? LaneOrigin
	{
		get
		{
			var playerOrigin = PlayerOrigin;
			if (playerOrigin == null)
			{
				return null;
			}

			if (laneOrigin == null)
			{
				laneOrigin = new GameObject("HSV Player Lane").transform;
			}

			if (laneOrigin.parent != playerOrigin)
			{
				laneOrigin.SetParent(playerOrigin, false);
			}

			return laneOrigin;
		}
	}

	public Vector3 PlayerOriginPosition => PlayerOrigin?.position ?? Vector3.zero;

	public Vector3 PlayerPointToWorld(Vector3 position) => LaneOrigin?.TransformPoint(position) ?? position;

	public void Initialize()
	{
		if (trackLaneRotation)
		{
			beatmapObjectManager.noteWasSpawnedEvent += NoteWasSpawned;
		}
	}

	public void Dispose()
	{
		if (trackLaneRotation)
		{
			beatmapObjectManager.noteWasSpawnedEvent -= NoteWasSpawned;
		}

		if (laneOrigin != null)
		{
			UnityEngine.Object.Destroy(laneOrigin.gameObject);
		}
	}

	public void LateTick()
	{
		var origin = LaneOrigin;
		if (origin != null)
		{
			origin.localRotation = CurrentLaneRotation();
		}
	}

	public void Attach(FlyingObjectEffect effect, Vector3 targetPosition, Quaternion worldRotation)
	{
		var origin = LaneOrigin;
		if (origin == null)
		{
			return;
		}

		var effectTransform = effect.transform;
		var startPosition = effectTransform.position;
		effectTransform.SetParent(origin, true);
		effect._startPos = origin.InverseTransformPoint(startPosition);
		effect._targetPos = origin.InverseTransformPoint(targetPosition);
		effect._rotation = Quaternion.Inverse(origin.rotation) * worldRotation;
		effectTransform.localPosition = effect._startPos;
		effectTransform.localRotation = effect._rotation;
	}

	private void NoteWasSpawned(NoteController noteController)
	{
		float time = noteController.noteData.time;
		int index = laneRotations.Count;
		while (index > 0 && laneRotations[index - 1].Time > time)
		{
			index--;
		}

		if ((index > 0 && Mathf.Approximately(laneRotations[index - 1].Time, time)) ||
		    (index < laneRotations.Count && Mathf.Approximately(laneRotations[index].Time, time)))
		{
			return;
		}

		laneRotations.Insert(index, new LaneRotation
		{
			Time = time,
			Rotation = noteController.worldRotation,
			TransitionStartTime = time,
			TransitionStartRotation = Quaternion.identity
		});
		for (int i = index; i < laneRotations.Count; i++)
		{
			var current = laneRotations[i];
			if (i > 0)
			{
				var previous = laneRotations[i - 1];
				if (Quaternion.Angle(previous.Rotation, current.Rotation) < 0.01f)
				{
					current.TransitionStartTime = previous.TransitionStartTime;
					current.TransitionStartRotation = previous.TransitionStartRotation;
				}
				else
				{
					current.TransitionStartTime = current.Time;
					current.TransitionStartRotation = Evaluate(previous, current.Time);
				}
			}

			laneRotations[i] = current;
		}
	}

	private Quaternion CurrentLaneRotation()
	{
		float songTime = audioTimeSyncController.songTime;
		int low = 0;
		int high = laneRotations.Count - 1;
		int current = -1;
		while (low <= high)
		{
			int middle = low + ((high - low) / 2);
			if (laneRotations[middle].Time <= songTime)
			{
				current = middle;
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}

		return current >= 0 ? Evaluate(laneRotations[current], songTime) : Quaternion.identity;
	}

	private static Quaternion Evaluate(LaneRotation laneRotation, float time)
	{
		float progress = Mathf.Clamp01((time - laneRotation.TransitionStartTime) / LaneTransitionSeconds);
		return Quaternion.Slerp(laneRotation.TransitionStartRotation, laneRotation.Rotation,
			Mathf.SmoothStep(0f, 1f, progress));
	}
}
