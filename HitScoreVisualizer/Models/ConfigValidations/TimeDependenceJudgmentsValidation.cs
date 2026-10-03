using System;
using System.Linq;
using UnityEngine;

namespace HitScoreVisualizer.Models.ConfigValidations;

internal class TimeDependenceJudgmentsValidation : IConfigValidation
{
	public bool IsValid(HsvConfigModel config) => IsValid(config, message => Plugin.Log.Warn(message));

	public bool IsValid(HsvConfigModel config, Action<string> warning)
	{
		var judgments = config.TimeDependenceJudgments;
		if (judgments is null || judgments.Count <= 1)
		{
			return true;
		}

		var isOrdered = judgments
			.Zip(judgments.Skip(1), (a, b) => a.Threshold > b.Threshold)
			.All(x => x);

		if (!isOrdered)
		{
			warning("Time dependence judgments are not correctly ordered; they should be ordered from highest to lowest threshold");
			return false;
		}

		var hasDuplicate = judgments
			.Zip(judgments.Skip(1), (a, b) => Mathf.Approximately(a.Threshold, b.Threshold))
			.Contains(true);

		if (hasDuplicate)
		{
			warning("Time dependence judgments contain a duplicate threshold");
			return false;
		}

		return true;
	}
}