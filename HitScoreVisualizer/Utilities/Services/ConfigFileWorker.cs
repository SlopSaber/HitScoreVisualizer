using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HitScoreVisualizer.Utilities.Services;

internal static class ConfigFileWorker
{
	private enum Operation { EnsureDirectories, Catalog, Exists, BeginRead, EndRead, MoveIfExists, Backup, Delete, OpenFolder, BeginSave, CommitSave, AbortSave }

	private sealed class Result
	{
		public string[] Paths = [];
		public string? Text;
		public bool Exists;
		public long Token;
	}

	private sealed class Request
	{
		public readonly Operation Operation;
		public readonly string Path;
		public readonly string? Destination;
		public readonly string? Text;
		public readonly long Token;
		public readonly TaskCompletionSource<Result> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Request(Operation operation, string path, string? destination = null, string? text = null, long token = 0)
		{
			Operation = operation;
			Path = path;
			Destination = destination;
			Text = text;
			Token = token;
		}
	}

	private static readonly object gate = new();
	private static readonly Queue<Request> requests = new();
	private static readonly Dictionary<long, Request> saveCompletions = new();
	private static readonly Dictionary<long, IDisposable> handles = new();
	private static Task? worker;
	private static long nextToken;
	private static long activeToken;

	public static Task EnsureDirectories(string configPath, string backupPath) => Enqueue(new(Operation.EnsureDirectories, configPath, backupPath));
	public static async Task<string[]> Catalog(string configPath, string backupPath) => (await Enqueue(new(Operation.Catalog, configPath, backupPath)).ConfigureAwait(false)).Paths;
	public static async Task<bool> Exists(string path) => (await Enqueue(new(Operation.Exists, path)).ConfigureAwait(false)).Exists;
	public static async Task<(long Token, string Text)> BeginRead(string path)
	{
		var result = await Enqueue(new(Operation.BeginRead, path)).ConfigureAwait(false);
		return (result.Token, result.Text!);
	}
	public static Task EndRead(long token) => FinishSave(new(Operation.EndRead, string.Empty, token: token));
	public static Task MoveIfExists(string source, string destination) => Enqueue(new(Operation.MoveIfExists, source, destination));
	public static Task Backup(string source, string destination) => Enqueue(new(Operation.Backup, source, destination));
	public static Task Delete(string path) => Enqueue(new(Operation.Delete, path));
	public static Task OpenFolder(string path) => Enqueue(new(Operation.OpenFolder, path));
	public static async Task<long> BeginSave(string path) => (await Enqueue(new(Operation.BeginSave, path)).ConfigureAwait(false)).Token;
	public static Task CommitSave(long token, string text) => FinishSave(new(Operation.CommitSave, string.Empty, text: text, token: token));
	public static Task AbortSave(long token) => FinishSave(new(Operation.AbortSave, string.Empty, token: token));

	private static Task<Result> Enqueue(Request request)
	{
		lock (gate)
		{
			requests.Enqueue(request);
			if (worker is null)
			{
				StartWorker();
			}
		}
		return request.Completion.Task;
	}

	private static Task<Result> FinishSave(Request request)
	{
		lock (gate)
		{
			if (request.Token != activeToken || activeToken == 0 || saveCompletions.ContainsKey(request.Token))
			{
				request.Completion.SetException(new InvalidOperationException("The configuration file operation is no longer active."));
			}
			else
			{
				saveCompletions.Add(request.Token, request);
				Monitor.PulseAll(gate);
			}
		}
		return request.Completion.Task;
	}

	private static void StartWorker()
	{
		worker = Task.Factory.StartNew(ProcessQueue, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
		worker.ContinueWith(Completed, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}

	private static void ProcessQueue()
	{
		while (true)
		{
			Request request;
			lock (gate)
			{
				if (requests.Count == 0)
				{
					return;
				}
				request = requests.Dequeue();
			}
			try
			{
				if (request.Operation is Operation.BeginSave or Operation.BeginRead)
				{
					ProcessHandle(request);
				}
				else
				{
					request.Completion.SetResult(Execute(request));
				}
			}
			catch (Exception error)
			{
				request.Completion.TrySetException(error);
			}
		}
	}

	private static void Completed(Task completed)
	{
		lock (gate)
		{
			if (!ReferenceEquals(worker, completed))
			{
				return;
			}
			worker = null;
			if (requests.Count != 0)
			{
				StartWorker();
			}
		}
	}

	private static Result Execute(Request request)
	{
		switch (request.Operation)
		{
			case Operation.EnsureDirectories:
				Directory.CreateDirectory(request.Path);
				Directory.CreateDirectory(request.Destination!);
				break;
			case Operation.Catalog:
				Directory.CreateDirectory(request.Path);
				Directory.CreateDirectory(request.Destination!);
				var paths = new List<string>();
				foreach (var extension in new[] { "json", "hsv", "hsvconfig" })
				{
					foreach (var file in new DirectoryInfo(request.Path).EnumerateFiles($"*.{extension}", SearchOption.AllDirectories))
					{
						paths.Add(file.FullName);
					}
				}
				return new() { Paths = paths.ToArray() };
			case Operation.Exists:
				return new() { Exists = new FileInfo(request.Path).Exists };
			case Operation.MoveIfExists:
				var source = new FileInfo(request.Path);
				if (source.Exists)
				{
					source.MoveTo(request.Destination!);
				}
				break;
			case Operation.Backup:
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(request.Destination!)!);
				new FileInfo(request.Path).CopyTo(FilePathUtils.GetUniqueFilePath(request.Destination!));
				break;
			case Operation.Delete:
				var deleted = new FileInfo(request.Path);
				if (deleted.Exists)
				{
					deleted.Delete();
				}
				break;
			case Operation.OpenFolder:
				Directory.CreateDirectory(request.Path);
				using (Process.Start(request.Path)) { }
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(request.Operation));
		}
		return new();
	}

	private static void ProcessHandle(Request begin)
	{
		var handle = begin.Operation is Operation.BeginSave
			? (IDisposable)new FileInfo(begin.Path).CreateText()
			: new FileInfo(begin.Path).OpenText();
		string? content;
		try
		{
			content = handle is StreamReader reader ? reader.ReadToEnd() : null;
		}
		catch
		{
			handle.Dispose();
			throw;
		}
		var token = ++nextToken;
		handles.Add(token, handle);
		lock (gate)
		{
			activeToken = token;
		}
		begin.Completion.SetResult(new() { Token = token, Text = content });

		// Keep the original file lock through owner JSON callbacks. Only the matching finish can pass this operation.
		Request finish;
		lock (gate)
		{
			while (!saveCompletions.TryGetValue(token, out finish!))
			{
				Monitor.Wait(gate);
			}
			saveCompletions.Remove(token);
		}

		Exception? error = null;
		try
		{
			using (handles[token])
			{
				if (finish.Operation is Operation.CommitSave)
				{
					((StreamWriter)handle).Write(finish.Text);
				}
			}
		}
		catch (Exception caught)
		{
			error = caught;
		}
		finally
		{
			handles.Remove(token);
			lock (gate)
			{
				activeToken = 0;
			}
		}
		if (error is null)
		{
			finish.Completion.SetResult(new());
		}
		else
		{
			finish.Completion.SetException(error);
		}
	}
}