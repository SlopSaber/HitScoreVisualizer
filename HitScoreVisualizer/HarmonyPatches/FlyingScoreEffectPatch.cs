using HitScoreVisualizer.Components;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Extensions;
using SiraUtil.Affinity;
using UnityEngine;

namespace HitScoreVisualizer.HarmonyPatches;

internal class FlyingScoreEffectPatch : IAffinity
{
	private readonly HsvConfigModel config;
	private readonly PlayerMovementFollower playerMovementFollower;

	private FlyingScoreEffectPatch(HsvConfigModel config, PlayerMovementFollower playerMovementFollower)
	{
		this.config = config;
		this.playerMovementFollower = playerMovementFollower;
	}

	// When the flying score effect spawns, InitAndPresent is called
	// When the post swing score changes - as the saber moves - HandleCutScoreBufferDidChange is called
	// When the post swing score stops changing - HandleCutScoreBufferDidFinish is called

	[AffinityPrefix]
	[AffinityPriority(1000)]
	[AffinityPatch(typeof(FlyingScoreEffect), nameof(FlyingScoreEffect.InitAndPresent))]
	internal bool InitAndPresent(ref FlyingScoreEffect __instance, IReadonlyCutScoreBuffer cutScoreBuffer, float duration, Vector3 targetPos)
	{
		var judgmentDetails = new JudgmentDetails(cutScoreBuffer)
		{
			AfterCutScore = config.AssumeMaxPostSwing ? cutScoreBuffer.noteScoreDefinition.maxAfterCutScore : cutScoreBuffer.afterCutScore,
		};

		var (text, color) = config.Judge(in judgmentDetails);
		__instance._text.text = text;
		__instance._color = color;
		__instance._cutScoreBuffer = cutScoreBuffer;
		__instance._maxCutDistanceScoreIndicator.enabled = false;
		__instance._colorAMultiplier = 1f;

		if (!cutScoreBuffer.isFinished)
		{
			cutScoreBuffer.RegisterDidChangeReceiver(__instance);
			cutScoreBuffer.RegisterDidFinishReceiver(__instance);
			__instance._registeredToCallbacks = true;
		}

		if (!playerMovementFollower.ShouldFollowEffects)
		{
			if (config.FixedPosition != null)
			{
				targetPos = config.FixedPosition.Value;
				__instance.transform.position = targetPos;
			}
			else if (config.TargetPositionOffset != null)
			{
				targetPos += config.TargetPositionOffset.Value;
			}

			__instance.InitAndPresent(duration, targetPos, cutScoreBuffer.noteCutInfo.worldRotation, false);
			return false;
		}

		if (config.FixedPosition != null)
		{
			// Set current and target position to the desired fixed position
			targetPos = playerMovementFollower.PlayerPointToWorld(config.FixedPosition.Value);
		}
		else
		{
			var rotation = cutScoreBuffer.noteCutInfo.worldRotation;
			var inverseRotation = cutScoreBuffer.noteCutInfo.inverseWorldRotation;
			var originPosition = playerMovementFollower.PlayerOriginPosition;
			var targetOffset = inverseRotation * targetPos;
			targetOffset.x = (inverseRotation * (cutScoreBuffer.noteCutInfo.cutPoint - originPosition)).x;
			targetPos = originPosition + rotation * targetOffset;
			if (config.TargetPositionOffset != null)
			{
				targetPos += config.TargetPositionOffset.Value;
			}
		}

		var spawnPosition = config.FixedPosition != null ? targetPos : __instance.transform.localPosition;
		__instance.transform.SetParent(null, true);
		__instance.transform.position = spawnPosition;
		__instance.InitAndPresent(duration, targetPos, cutScoreBuffer.noteCutInfo.worldRotation, false);
		playerMovementFollower.Attach(__instance, targetPos, cutScoreBuffer.noteCutInfo.worldRotation,
			config.FixedPosition != null, playerMovementFollower.ParentForCut(cutScoreBuffer.noteCutInfo.noteData));

		return false;
	}

	[AffinityPrefix]
	[AffinityPatch(typeof(FlyingScoreSpawner), nameof(FlyingScoreSpawner.HandleFlyingObjectEffectDidFinish))]
	private void ReturnToPool(FlyingObjectEffect flyingObjectEffect)
	{
		if (playerMovementFollower.ShouldFollowEffects)
		{
			flyingObjectEffect.transform.SetParent(null, true);
		}
	}

	[AffinityPrefix]
	[AffinityPatch(typeof(FlyingScoreEffect), nameof(FlyingScoreEffect.HandleCutScoreBufferDidChange))]
	internal bool HandleCutScoreBufferDidChange(FlyingScoreEffect __instance, CutScoreBuffer cutScoreBuffer)
	{
		if (!config.DoIntermediateUpdates)
		{
			return false;
		}

		var judgmentDetails = new JudgmentDetails(cutScoreBuffer);
		var (text, color) = config.Judge(in judgmentDetails);
		__instance._text.text = text;
		__instance._color = color;

		return false;
	}

	[AffinityPrefix]
	[AffinityPatch(typeof(FlyingScoreEffect), nameof(FlyingScoreEffect.HandleCutScoreBufferDidFinish))]
	internal void HandleCutScoreBufferDidFinish(FlyingScoreEffect __instance, CutScoreBuffer cutScoreBuffer)
	{
		var judgmentDetails = new JudgmentDetails(cutScoreBuffer);
		var (text, color) = config.Judge(in judgmentDetails);
		__instance._text.text = text;
		__instance._color = color;
	}
}
