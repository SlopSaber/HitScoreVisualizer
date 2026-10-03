using System.Text;
using HitScoreVisualizer.Models;
using HitScoreVisualizer.Utilities.Services;
using UnityEngine;

namespace HitScoreVisualizer.Utilities.Extensions;

internal static class ScoreJudgment
{
	public static (string hitScoreText, Color hitScoreColor) Judge(this HsvConfigModel config, in JudgmentDetails details)
	{
		return details.CutInfo.noteData.gameplayType switch
		{
			NoteData.GameplayType.Normal => GetNormalDisplay(config, in details),
			NoteData.GameplayType.BurstSliderHead => GetChainHeadDisplay(config, in details),
			NoteData.GameplayType.BurstSliderElement => GetChainSegmentDisplay(config, in details),
			_ => (string.Empty, Color.white),
		};
	}

	private static (string, Color) GetNormalDisplay(this HsvConfigModel config, in JudgmentDetails details)
	{
		var judgment = NormalJudgment.Default;
		var fadeJudgment = NormalJudgment.Default;

		for (var i = 0; i < config.Judgments.Count; i++)
		{
			if (config.Judgments[i].Threshold > details.TotalCutScore)
			{
				continue;
			}

			judgment = config.Judgments[i];
			if (i > 0)
			{
				fadeJudgment = config.Judgments[i - 1];
			}
			break;
		}

		var color = judgment.Fade
			? Color.Lerp(
				judgment.Color,
				fadeJudgment.Color,
				Mathf.InverseLerp(judgment.Threshold, fadeJudgment.Threshold, details.TotalCutScore))
			: judgment.Color;

		var text = FormatJudgmentTextByMode(config, judgment.Text, in details);

		return (text, color);
	}

	private static (string, Color) GetChainHeadDisplay(this HsvConfigModel config, in JudgmentDetails details)
	{
		var judgment = ChainHeadJudgment.Default;
		var fadeJudgment = ChainHeadJudgment.Default;

		for (var i = 0; i < config.ChainHeadJudgments.Count; i++)
		{
			if (config.ChainHeadJudgments[i].Threshold > details.TotalCutScore)
			{
				continue;
			}

			judgment = config.ChainHeadJudgments[i];
			if (i > 0)
			{
				fadeJudgment = config.ChainHeadJudgments[i - 1];
			}
			break;
		}

		var color = !judgment.Fade ? judgment.Color
			: Color.Lerp(judgment.Color, fadeJudgment.Color, Mathf.InverseLerp(judgment.Threshold, fadeJudgment.Threshold, details.TotalCutScore));

		var text = FormatJudgmentTextByMode(config, judgment.Text, in details);

		return (text, color);
	}

	private static (string, Color) GetChainSegmentDisplay(this HsvConfigModel config, in JudgmentDetails details)
	{
		var chainLinkDisplay = config.ChainLinkDisplay ?? ChainLinkDisplay.Default;
		var text = FormatJudgmentTextByMode(config, chainLinkDisplay.Text, in details);
		return (text, chainLinkDisplay.Color);
	}

	private static string FormatJudgmentTextByMode(this HsvConfigModel config, string unformattedText, in JudgmentDetails details)
	{
		return config.DisplayMode switch
		{
			DisplayMode.Format => FormatJudgmentText(config, unformattedText, in details),
			DisplayMode.TextOnly => unformattedText,
			DisplayMode.Numeric => details.TotalCutScore.ToString(),
			DisplayMode.ScoreOnTop => $"{details.TotalCutScore}\n{unformattedText}\n",
			DisplayMode.Directions => $"{unformattedText}\n{details.CutInfo.CalculateOffDirection().ToFormattedDirection()}\n",
			_ => $"{unformattedText}\n{details.TotalCutScore}\n"
		};
	}

	private static string FormatJudgmentText(this HsvConfigModel config, string unformattedText, in JudgmentDetails details)
	{
		var formattedBuilder = new StringBuilder();
		var template = JudgmentTemplateCache.Get(unformattedText);

		var timeDependence = Mathf.Abs(details.CutInfo.cutNormal.z);

		for (var i = 0; i < template.Count; i++)
		{
			var part = template[i];
			if (!part.IsSpecifier)
			{
				formattedBuilder.Append(part.Text);
				continue;
			}

			formattedBuilder.Append(part.Specifier switch
			{
				'b' => details.BeforeCutScore,
				'c' => details.CenterCutScore,
				'a' => details.AfterCutScore,
				't' => (timeDependence * Mathf.Pow(10, config.TimeDependenceDecimalOffset)).ToString($"n{config.TimeDependenceDecimalPrecision}"),
				'B' => config.BeforeCutAngleJudgments.JudgeSegment(details.BeforeCutScore),
				'C' => config.AccuracyJudgments.JudgeSegment(details.CenterCutScore),
				'A' => config.AfterCutAngleJudgments.JudgeSegment(details.AfterCutScore),
				'T' => config.TimeDependenceJudgments.JudgeTimeDependenceSegment(timeDependence, config.TimeDependenceDecimalOffset, config.TimeDependenceDecimalPrecision),
				'd' => details.CutInfo.CalculateOffDirection().ToFormattedDirection(),
				's' => details.TotalCutScore,
				'p' => $"{(double) details.TotalCutScore / details.MaxPossibleScore * 100:0}",
				'%' => "%",
				'n' => "\n",
				_ => $"%{part.Specifier}"
			});
		}

		return formattedBuilder.ToString();
	}
}
