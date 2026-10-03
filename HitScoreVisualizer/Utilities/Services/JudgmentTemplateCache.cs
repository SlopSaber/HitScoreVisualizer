using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HitScoreVisualizer.Models;

namespace HitScoreVisualizer.Utilities.Services;

internal static class JudgmentTemplateCache
{
	private const int SharedTemplateLimit = 256;
	private static readonly object gate = new();
	private static readonly ConditionalWeakTable<string, Task<Template>> identities = new();
	private static readonly Dictionary<string, Task<Template>> templates = new(StringComparer.Ordinal);
	private static readonly Queue<string> insertionOrder = new();
	private static Task tail = Task.CompletedTask;

	internal static Template Get(string text)
	{
		if (text is null)
		{
			throw new NullReferenceException();
		}
		var task = Prepare(text);
		if (!task.IsCompleted)
		{
			((IAsyncResult)task).AsyncWaitHandle.WaitOne();
		}
		return task.GetAwaiter().GetResult();
	}

	internal static void Prewarm(HsvConfigModel config)
	{
		if (config.DisplayMode != DisplayMode.Format)
		{
			return;
		}
		if (config.Judgments is { } normal)
		{
			foreach (var judgment in normal)
			{
				Prewarm(judgment?.Text);
			}
		}
		if (config.ChainHeadJudgments is { } heads)
		{
			foreach (var judgment in heads)
			{
				Prewarm(judgment?.Text);
			}
		}
		Prewarm(config.ChainLinkDisplay?.Text);
		if (config.TimeDependenceJudgments is { } timeDependence)
		{
			foreach (var judgment in timeDependence)
			{
				Prewarm(judgment?.Text);
			}
		}
	}

	private static void Prewarm(string? text)
	{
		if (text is not null)
		{
			_ = Prepare(text);
		}
	}

	private static Task<Template> Prepare(string text)
	{
		if (identities.TryGetValue(text, out var task))
		{
			return task;
		}
		lock (gate)
		{
			if (identities.TryGetValue(text, out task))
			{
				return task;
			}
			if (!templates.TryGetValue(text, out task))
			{
				// The queue accepts only immutable text, including on synchronous cold misses.
				var flowSuppressed = ExecutionContext.IsFlowSuppressed();
				var flow = default(AsyncFlowControl);
				try
				{
					if (!flowSuppressed)
					{
						flow = ExecutionContext.SuppressFlow();
					}
					task = tail.ContinueWith((_, state) => Compile((string)state!), text,
						CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
					tail = task;
					_ = task.ContinueWith(completed =>
					{
						if (completed.IsFaulted)
						{
							_ = completed.Exception;
						}
						lock (gate)
						{
							if (ReferenceEquals(tail, completed))
							{
								tail = Task.CompletedTask;
							}
						}
					}, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
				}
				finally
				{
					if (!flowSuppressed)
					{
						flow.Undo();
					}
				}
				templates.Add(text, task);
				insertionOrder.Enqueue(text);
				if (templates.Count > SharedTemplateLimit)
				{
					templates.Remove(insertionOrder.Dequeue());
				}
			}
			identities.Add(text, task);
			return task;
		}
	}

	private static Template Compile(string text)
	{
		var parts = new List<Part>();
		var position = 0;
		int nextPercent;
		while ((nextPercent = text.IndexOf('%', position)) != -1)
		{
			if (nextPercent > position)
			{
				parts.Add(new Part(text.Substring(position, nextPercent - position)));
			}
			parts.Add(new Part(nextPercent + 1 < text.Length ? text[nextPercent + 1] : ' '));
			position = nextPercent + 2;
			if (position >= text.Length)
			{
				break;
			}
		}
		if (position < text.Length)
		{
			parts.Add(new Part(text.Substring(position)));
		}
		return new Template(parts.ToArray());
	}

	internal sealed class Template
	{
		private readonly Part[] parts;
		internal int Count => parts.Length;
		internal Part this[int index] => parts[index];

		internal Template(Part[] parts)
		{
			this.parts = parts;
		}
	}

	internal readonly struct Part
	{
		internal string Text { get; }
		internal char Specifier { get; }
		internal bool IsSpecifier { get; }

		internal Part(string text)
		{
			Text = text;
			Specifier = default;
			IsSpecifier = false;
		}

		internal Part(char specifier)
		{
			Text = new string(new[] { '%', specifier });
			Specifier = specifier;
			IsSpecifier = true;
		}
	}
}
