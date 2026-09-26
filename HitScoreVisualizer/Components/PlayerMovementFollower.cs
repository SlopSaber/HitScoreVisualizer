using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Zenject;

namespace HitScoreVisualizer.Components;

internal class PlayerMovementFollower : IInitializable, ILateTickable, IDisposable
{
	private readonly record struct LaneSample(float Time, Quaternion Rotation, string[] Tracks);

	private readonly PlayerTransforms playerTransforms;
	private readonly BeatmapObjectManager beatmapObjectManager;
	private readonly AudioTimeSyncController audioTimeSyncController;
	private readonly List<LaneSample> laneSamples = new();
	private readonly Dictionary<NoteData, Transform> cutParents = new();
	private readonly IDictionary? heckTracks;
	private readonly MethodInfo? getTrackProperty;
	private readonly object?[] trackPropertyArguments = new object?[2];
	private readonly bool leftHanded;
	private readonly bool hasLaneRotation;
	private Transform? laneOrigin;

	public PlayerMovementFollower(PlayerTransforms playerTransforms, BeatmapObjectManager beatmapObjectManager,
		AudioTimeSyncController audioTimeSyncController,
		IReadonlyBeatmapData beatmapData, GameplayCoreSceneSetupData sceneData, DiContainer container)
	{
		this.playerTransforms = playerTransforms;
		this.beatmapObjectManager = beatmapObjectManager;
		this.audioTimeSyncController = audioTimeSyncController;
		leftHanded = sceneData.playerSpecificSettings.leftHanded;

		var trackType = Type.GetType("Heck.Animation.Track, Heck");
		var extensionsType = Type.GetType("Heck.Animation.AnimationExtensions, Heck");
		if (trackType != null && extensionsType != null)
		{
			var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), trackType);
			heckTracks = container.TryResolve(dictionaryType) as IDictionary;
			getTrackProperty = extensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.FirstOrDefault(method => method.Name == "GetProperty" && method.IsGenericMethodDefinition)
				?.MakeGenericMethod(typeof(Quaternion));
		}

		if (sceneData.beatmapKey.characteristic is BeatmapCharacteristic.Degree90 or BeatmapCharacteristic.Degree360)
		{
			return;
		}

		var customDataProperties = new Dictionary<Type, PropertyInfo?>();
		foreach (var note in beatmapData.GetBeatmapDataItems<NoteData>(0).OrderBy(note => note.time))
		{
			if (note.gameplayType == NoteData.GameplayType.Bomb ||
			    (laneSamples.Count > 0 && Mathf.Approximately(laneSamples[laneSamples.Count - 1].Time, note.time)))
			{
				continue;
			}

			var noteType = note.GetType();
			if (!customDataProperties.TryGetValue(noteType, out var customDataProperty))
			{
				customDataProperty = noteType.GetProperty("customData");
				customDataProperties.Add(noteType, customDataProperty);
			}
			var customData = customDataProperty?.GetValue(note) as IDictionary<string, object>;
			laneSamples.Add(new LaneSample(note.time, ReadRotation(customData), ReadTracks(customData)));
		}

		hasLaneRotation = laneSamples.Any(sample => Quaternion.Angle(sample.Rotation, Quaternion.identity) > 0.01f ||
		                                         sample.Tracks.Length > 0);
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
				laneOrigin.localRotation = CurrentLaneRotation();
			}

			if (laneOrigin.parent != playerOrigin)
			{
				laneOrigin.SetParent(playerOrigin, false);
			}

			return laneOrigin;
		}
	}

	public bool HasPlayerTrack
	{
		get
		{
			for (var ancestor = PlayerOrigin; ancestor != null; ancestor = ancestor.parent)
			{
				if (ancestor.name == "NoodlePlayerTransformRoot")
				{
					return true;
				}
			}

			return false;
		}
	}

	public bool ShouldFollowEffects => HasPlayerTrack || hasLaneRotation;
	public Vector3 PlayerOriginPosition => PlayerOrigin?.position ?? Vector3.zero;
	public Vector3 PlayerPointToWorld(Vector3 position) => LaneOrigin?.TransformPoint(position) ?? position;
	public Transform? ParentForCut(NoteData noteData)
	{
		if (!cutParents.TryGetValue(noteData, out var parent))
		{
			return null;
		}

		cutParents.Remove(noteData);
		return parent != null ? parent : null;
	}

	public static Transform? MapParent(NoteController noteController)
	{
		var parent = noteController.transform.parent;
		return parent != null && parent.name == "ParentObject" ? parent : null;
	}

	public void Initialize()
	{
		beatmapObjectManager.noteWasCutEvent += NoteWasCut;
	}

	private void NoteWasCut(NoteController noteController, in NoteCutInfo noteCutInfo)
	{
		var parent = MapParent(noteController);
		if (parent != null)
		{
			cutParents[noteCutInfo.noteData] = parent;
		}
	}

	public void LateTick()
	{
		if (laneOrigin != null)
		{
			laneOrigin.localRotation = CurrentLaneRotation();
		}
	}

	public void Dispose()
	{
		beatmapObjectManager.noteWasCutEvent -= NoteWasCut;
		if (laneOrigin != null)
		{
			UnityEngine.Object.Destroy(laneOrigin.gameObject);
		}
	}

	public void Attach(FlyingObjectEffect effect, Vector3 targetPosition, Quaternion worldRotation,
		bool fixedPosition = false, Transform? mapParent = null)
	{
		var origin = mapParent != null && !fixedPosition ? mapParent : LaneOrigin;
		if (origin == null)
		{
			return;
		}

		if (mapParent != null && !fixedPosition)
		{
			worldRotation = mapParent.rotation * worldRotation;
		}
		else if (hasLaneRotation)
		{
			var laneRotation = CurrentLaneRotation();
			if (!fixedPosition)
			{
				var playerPosition = PlayerOriginPosition;
				targetPosition = playerPosition + laneRotation * Quaternion.Inverse(worldRotation) *
					(targetPosition - playerPosition);
			}

			worldRotation = (PlayerOrigin?.rotation ?? Quaternion.identity) * laneRotation;
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

	private Quaternion CurrentLaneRotation()
	{
		if (laneSamples.Count == 0)
		{
			return Quaternion.identity;
		}

		float songTime = audioTimeSyncController.songTime;
		int low = 0;
		int high = laneSamples.Count - 1;
		int current = -1;
		while (low <= high)
		{
			int middle = low + ((high - low) / 2);
			if (laneSamples[middle].Time <= songTime)
			{
				current = middle;
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}

		if (current < 0)
		{
			return SampleRotation(laneSamples[0]);
		}

		var from = laneSamples[current];
		var fromRotation = SampleRotation(from);
		if (current == laneSamples.Count - 1)
		{
			return fromRotation;
		}

		var to = laneSamples[current + 1];
		if (heckTracks != null && from.Tracks.Length > 0 && from.Tracks.SequenceEqual(to.Tracks))
		{
			return fromRotation;
		}

		return Quaternion.Slerp(fromRotation, SampleRotation(to), Mathf.InverseLerp(from.Time, to.Time, songTime));
	}

	private Quaternion SampleRotation(LaneSample sample)
	{
		var rotation = sample.Rotation;
		if (heckTracks == null || getTrackProperty == null)
		{
			return rotation;
		}

		trackPropertyArguments[1] = "offsetWorldRotation";
		foreach (var trackName in sample.Tracks)
		{
			if (!heckTracks.Contains(trackName)) continue;
			trackPropertyArguments[0] = heckTracks[trackName];
			if (getTrackProperty.Invoke(null, trackPropertyArguments) is Quaternion offset)
			{
				rotation *= Mirror(offset);
			}
		}

		return rotation;
	}

	private Quaternion ReadRotation(IDictionary<string, object>? customData)
	{
		if (customData == null ||
		    (!customData.TryGetValue("worldRotation", out var value) &&
		     !customData.TryGetValue("_rotation", out value)) || value == null)
		{
			return Quaternion.identity;
		}

		var rotation = value is IList angles && angles.Count >= 3
			? Quaternion.Euler(Convert.ToSingle(angles[0]), Convert.ToSingle(angles[1]), Convert.ToSingle(angles[2]))
			: Quaternion.Euler(0f, Convert.ToSingle(value), 0f);
		return Mirror(rotation);
	}

	private Quaternion Mirror(Quaternion rotation) => leftHanded
		? new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w)
		: rotation;

	private static string[] ReadTracks(IDictionary<string, object>? customData)
	{
		if (customData == null ||
		    (!customData.TryGetValue("track", out var value) &&
		     !customData.TryGetValue("_track", out value)) || value == null)
		{
			return [];
		}

		if (value is string name)
		{
			return [name];
		}

		return value is IEnumerable names ? names.Cast<object>().OfType<string>().ToArray() : [];
	}
}
