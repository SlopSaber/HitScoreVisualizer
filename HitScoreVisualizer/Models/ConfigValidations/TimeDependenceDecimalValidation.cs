using System;

namespace HitScoreVisualizer.Models.ConfigValidations;

internal class TimeDependenceDecimalValidation : IConfigValidation
{
	public bool IsValid(HsvConfigModel config) => IsValid(config, message => Plugin.Log.Warn(message));

	public bool IsValid(HsvConfigModel config, Action<string> warning)
	{
		// 99 is the max for NumberFormatInfo.NumberDecimalDigits
		if (config.TimeDependenceDecimalPrecision is < 0 or > 99)
		{
			warning("timeDependencyDecimalPrecision is outside the range 0 to 99");
			return false;
		}

		if (config.TimeDependenceDecimalOffset < 0 || config.TimeDependenceDecimalOffset > Math.Log10(float.MaxValue))
		{
			warning($"timeDependencyDecimalOffset value is outside the range 0 to {(int) Math.Log10(float.MaxValue)}");
			return false;
		}

		return true;
	}
}