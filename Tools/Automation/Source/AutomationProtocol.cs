using System.Collections.Generic;
using System.Text;

namespace WarudoAutomation
{
	// The line protocol shared with the MikanXR editor's automation server, so one client drives
	// both: newline-terminated command lines tokenized on spaces and tabs with double-quoted spans,
	// answered by a count line and then exactly that many content lines.
	public static class AutomationProtocol
	{
		// Splits a command line into tokens. A double-quoted span groups spaces into one token,
		// with \" and \\ as the only escapes inside quotes.
		public static bool TokenizeCommandLine(string line, List<string> outTokens, out string outError)
		{
			outTokens.Clear();
			outError = null;

			var token = new StringBuilder();
			bool inToken = false;
			bool inQuotes = false;

			for (int i = 0; i < line.Length; ++i)
			{
				char c = line[i];

				if (inQuotes)
				{
					if (c == '\\' && i + 1 < line.Length && (line[i + 1] == '"' || line[i + 1] == '\\'))
					{
						token.Append(line[i + 1]);
						++i;
					}
					else if (c == '"')
					{
						inQuotes = false;
					}
					else
					{
						token.Append(c);
					}
				}
				else if (c == '"')
				{
					inQuotes = true;
					inToken = true;
				}
				else if (c == ' ' || c == '\t')
				{
					if (inToken)
					{
						outTokens.Add(token.ToString());
						token.Clear();
						inToken = false;
					}
				}
				else
				{
					token.Append(c);
					inToken = true;
				}
			}

			if (inQuotes)
			{
				outTokens.Clear();
				outError = "unterminated quote";
				return false;
			}

			if (inToken)
			{
				outTokens.Add(token.ToString());
			}

			return true;
		}

		// Frames a reply. Entries holding newlines split into several lines so the count line
		// always matches the number of lines the client reads.
		public static string FrameReply(IList<string> contentLines)
		{
			var flatLines = new List<string>();
			foreach (string entry in contentLines)
			{
				foreach (string part in (entry ?? string.Empty).Split('\n'))
				{
					flatLines.Add(part.EndsWith("\r") ? part.Substring(0, part.Length - 1) : part);
				}
			}

			var reply = new StringBuilder();
			reply.Append(flatLines.Count).Append("\r\n");
			foreach (string lineText in flatLines)
			{
				reply.Append(lineText).Append("\r\n");
			}

			return reply.ToString();
		}

		// The raw rest of a command line after the first tokenCount tokens, for commands that take
		// free text (quotes pass through verbatim)
		public static string RemainderAfterTokens(string line, int tokenCount)
		{
			int pos = 0;
			int tokensSkipped = 0;

			while (tokensSkipped < tokenCount && pos < line.Length)
			{
				while (pos < line.Length && (line[pos] == ' ' || line[pos] == '\t'))
				{
					++pos;
				}
				if (pos >= line.Length)
				{
					break;
				}

				bool inQuotes = false;
				while (pos < line.Length)
				{
					char c = line[pos];
					if (inQuotes)
					{
						if (c == '\\' && pos + 1 < line.Length && (line[pos + 1] == '"' || line[pos + 1] == '\\'))
						{
							++pos;
						}
						else if (c == '"')
						{
							inQuotes = false;
						}
					}
					else if (c == '"')
					{
						inQuotes = true;
					}
					else if (c == ' ' || c == '\t')
					{
						break;
					}

					++pos;
				}

				++tokensSkipped;
			}

			while (pos < line.Length && (line[pos] == ' ' || line[pos] == '\t'))
			{
				++pos;
			}

			return line.Substring(pos);
		}
	}
}
