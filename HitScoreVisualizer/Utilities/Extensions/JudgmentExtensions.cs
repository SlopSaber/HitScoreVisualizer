using System.Collections.Generic;
using System.Text;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Services;
using UnityEngine;

namespace HitScoreVisualizer.Utilities.Extensions;

internal static class JudgmentExtensions
{
	public static string JudgeSegment(this IList<JudgmentSegment>? judgments, int scoreForSegment)
	{
		if (judgments == null)
		{
			return string.Empty;
		}

		foreach (var j in judgments)
		{
			if (scoreForSegment >= j.Threshold)
			{
				return j.Text ?? string.Empty;
			}
		}

		return string.Empty;
	}

	public static string JudgeTimeDependenceSegment(this IList<TimeDependenceJudgmentSegment>? judgments, float scoreForSegment, int tdDecimalOffset, int tdDecimalPrecision)
	{
		if (judgments == null)
		{
			return string.Empty;
		}

		foreach (var j in judgments)
		{
			if (scoreForSegment >= j.Threshold)
			{
				return j.Text != null
					? FormatTimeDependenceSegment(j.Text, scoreForSegment, tdDecimalOffset, tdDecimalPrecision)
					: string.Empty;
			}
		}

		return string.Empty;
	}

	private static string FormatTimeDependenceSegment(string unformattedText, float timeDependence, int tdDecimalOffset, int tdDecimalPrecision)
	{
		var builder = new StringBuilder();
		var template = JudgmentTemplateCache.Get(unformattedText);
		for (var i = 0; i < template.Count; i++)
		{
			var part = template[i];
			if (!part.IsSpecifier)
			{
				builder.Append(part.Text);
				continue;
			}

			switch (part.Specifier)
			{
				case 't':
					builder.Append(ConvertTimeDependencePrecision(timeDependence, tdDecimalOffset, tdDecimalPrecision));
					break;
				case '%':
					builder.Append("%");
					break;
				case 'n':
					builder.Append("\n");
					break;
				default:
					builder.Append(part.Text);
					break;
			}
		}

		return builder.ToString();
	}

	private static string ConvertTimeDependencePrecision(float timeDependence, int decimalOffset, int decimalPrecision)
	{
		return (timeDependence * Mathf.Pow(10, decimalOffset)).ToString($"n{decimalPrecision}");
	}
}
