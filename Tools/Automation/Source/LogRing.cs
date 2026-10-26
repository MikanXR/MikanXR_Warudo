using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarudoAutomation
{
	public enum LogLevel
	{
		Info = 0,
		Warning = 1,
		Error = 2,
	}

	public struct LogEntry
	{
		public long Sequence;
		public DateTime Time;
		public LogLevel Level;
		public LogType Type;
		public string Message;
		public string StackTrace;
	}

	// The most recent Unity log messages from every thread, numbered so a drive can read only
	// what happened after a mark
	public static class LogRing
	{
		public const int Capacity = 4000;

		private static readonly object s_lock = new object();
		private static readonly Queue<LogEntry> s_entries = new Queue<LogEntry>();
		private static long s_nextSequence = 1;
		private static bool s_hooked;

		public static void Hook()
		{
			if (s_hooked)
			{
				return;
			}
			s_hooked = true;
			Application.logMessageReceivedThreaded += OnLogMessage;
		}

		// A hot reload loads a new copy of this class, so the old copy lets go of the log
		public static void Unhook()
		{
			if (!s_hooked)
			{
				return;
			}
			s_hooked = false;
			Application.logMessageReceivedThreaded -= OnLogMessage;
		}

		public static long LastSequence
		{
			get
			{
				lock (s_lock)
				{
					return s_nextSequence - 1;
				}
			}
		}

		public static bool TryParseLevel(string text, out LogLevel level)
		{
			switch (text.ToLowerInvariant())
			{
				case "info":
				case "log":
					level = LogLevel.Info;
					return true;
				case "warning":
				case "warn":
					level = LogLevel.Warning;
					return true;
				case "error":
					level = LogLevel.Error;
					return true;
				default:
					level = LogLevel.Info;
					return false;
			}
		}

		public static List<LogEntry> Snapshot(long afterSequence, LogLevel minLevel)
		{
			var result = new List<LogEntry>();
			lock (s_lock)
			{
				foreach (LogEntry entry in s_entries)
				{
					if (entry.Sequence > afterSequence && entry.Level >= minLevel)
					{
						result.Add(entry);
					}
				}
			}
			return result;
		}

		public static void Clear()
		{
			lock (s_lock)
			{
				s_entries.Clear();
			}
		}

		private static void OnLogMessage(string message, string stackTrace, LogType type)
		{
			LogLevel level;
			switch (type)
			{
				case LogType.Warning:
					level = LogLevel.Warning;
					break;
				case LogType.Error:
				case LogType.Assert:
				case LogType.Exception:
					level = LogLevel.Error;
					break;
				default:
					level = LogLevel.Info;
					break;
			}

			lock (s_lock)
			{
				s_entries.Enqueue(new LogEntry
				{
					Sequence = s_nextSequence++,
					Time = DateTime.Now,
					Level = level,
					Type = type,
					Message = message,
					StackTrace = stackTrace,
				});

				while (s_entries.Count > Capacity)
				{
					s_entries.Dequeue();
				}
			}
		}
	}
}
