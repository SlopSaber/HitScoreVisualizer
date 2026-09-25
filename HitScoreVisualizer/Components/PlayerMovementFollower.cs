using UnityEngine;

namespace HitScoreVisualizer.Components;

internal class PlayerMovementFollower
{
	private readonly PlayerTransforms playerTransforms;

	public PlayerMovementFollower(PlayerTransforms playerTransforms)
	{
		this.playerTransforms = playerTransforms;
	}

	private Transform? PlayerOrigin => playerTransforms._originTransform.parent;
	public Vector3 PlayerOriginPosition => PlayerOrigin?.position ?? Vector3.zero;

	public Vector3 PlayerPointToWorld(Vector3 position) => PlayerOrigin?.TransformPoint(position) ?? position;

	public void Attach(FlyingObjectEffect effect, Vector3 targetPosition, Quaternion worldRotation)
	{
		var origin = PlayerOrigin;
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
}
