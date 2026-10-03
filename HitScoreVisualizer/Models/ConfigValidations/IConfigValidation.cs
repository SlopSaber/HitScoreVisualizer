using System;

namespace HitScoreVisualizer.Models.ConfigValidations;

internal interface IConfigValidation
{
	bool IsValid(HsvConfigModel config);
	bool IsValid(HsvConfigModel config, Action<string> warning);
}