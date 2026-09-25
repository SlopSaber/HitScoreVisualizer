using UnityEngine;
using Zenject;

namespace HitScoreVisualizer.Components;

internal class HsvFlyingEffectSpawner : MonoBehaviour, IFlyingObjectEffectDidFinishEvent
{
	public record InitData(float Duration, float XSpread, float TargetYPos, float TargetZPos, Color Color, float FontSize);

	private float duration;
	private float xSpread;
	private float targetYPos;
	private float targetZPos;
	private Color color;
	private float fontSize;
	private HsvFlyingEffect.Pool missTextEffectPool = null!;
	private PluginConfig pluginConfig = null!;
	private PlayerMovementFollower playerMovementFollower = null!;

	[Inject]
	public void Init(InitData initData, HsvFlyingEffect.Pool effectPool, PluginConfig config, PlayerMovementFollower movementFollower)
	{
		duration = initData.Duration;
		xSpread = initData.XSpread;
		targetYPos = initData.TargetYPos;
		targetZPos = initData.TargetZPos;
		color = initData.Color;
		fontSize = initData.FontSize;
		missTextEffectPool = effectPool;
		pluginConfig = config;
		playerMovementFollower = movementFollower;
	}

	public void SpawnText(Vector3 pos, Quaternion rotation, Quaternion inverseRotation, string text, Color? color)
	{
		var missTextEffect = missTextEffectPool.Spawn();
		missTextEffect.didFinishEvent.Add(this);
		if (!playerMovementFollower.ShouldFollowEffects)
		{
			missTextEffect.transform.localPosition = pos;
			var originalTargetPos = rotation * new Vector3(Mathf.Sign((inverseRotation * pos).x) * xSpread, targetYPos, targetZPos);
			missTextEffect.InitAndPresent(text, duration, originalTargetPos, rotation, color ?? this.color, fontSize, false);
			return;
		}

		missTextEffect.transform.SetParent(null, true);
		missTextEffect.transform.position = pos;

		var originPosition = playerMovementFollower.PlayerOriginPosition;
		var targetPos = originPosition + rotation * new Vector3(Mathf.Sign((inverseRotation * (pos - originPosition)).x) * xSpread, targetYPos, targetZPos);

		missTextEffect.InitAndPresent(text, duration, targetPos, rotation, color ?? this.color, fontSize, false);
		playerMovementFollower.Attach(missTextEffect, targetPos, rotation);
	}

	public void HandleFlyingObjectEffectDidFinish(FlyingObjectEffect flyingObjectEffect)
	{
		flyingObjectEffect.didFinishEvent.Remove(this);
		if (playerMovementFollower.ShouldFollowEffects)
		{
			flyingObjectEffect.transform.SetParent(null, true);
		}
		missTextEffectPool.Despawn((HsvFlyingEffect)flyingObjectEffect);
	}
}
