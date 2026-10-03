using System;
using System.Collections.Generic;
using System.Linq;

namespace HitScoreVisualizer.Models.ConfigValidations;

internal class JudgmentsValidation : IConfigValidation
{
	private readonly Func<HsvConfigModel, IEnumerable<IJudgment>> propertyGetter;

	public JudgmentsValidation(Func<HsvConfigModel, IEnumerable<IJudgment>> propertyGetter)
	{
		this.propertyGetter = propertyGetter;
	}

	public bool IsValid(HsvConfigModel config) => IsValid(config, message => Plugin.Log.Warn(message));

	public bool IsValid(HsvConfigModel config, Action<string> warning)
	{
		var judgments = propertyGetter(config).ToList();
		if (judgments is [])
		{
			warning("Config contains no Judgments when it should specify at least one Judgment");
			return false;
		}

		var isOrdered = judgments
			.Zip(judgments.Skip(1), (a, b) => a.Threshold > b.Threshold)
			.All(x => x);

		if (!isOrdered)
		{
			warning("Judgments are not correctly ordered; they should be ordered from highest to lowest threshold");
			return false;
		}

		var isHighestJudgmentValid = !judgments.First().Fade;

		var hasDuplicates = judgments.Count > 1 && judgments
			.Zip(judgments.Skip(1), (a, b) => a.Threshold == b.Threshold)
			.Contains(true);

		if (!isHighestJudgmentValid)
		{
			warning("The first judgment cannot have fade set to true");
		}

		if (hasDuplicates)
		{
			warning("Judgments contain a duplicate threshold");
		}

		return isHighestJudgmentValid && !hasDuplicates;
	}
}