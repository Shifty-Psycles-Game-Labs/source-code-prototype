using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class NmuParser
{
    private static readonly Regex HeaderPattern = new(
        @"^@nmu\\s*\\((?<attributes>.*)\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlockPattern = new(
        @"^::\\s*(?<name>[A-Za-z][A-Za-z0-9_-]*)\\s*(?:\\((?<attributes>.*)\\))?\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExportPattern = new(
        @"^@export\\s*\\(\\s*(?<target>[A-Za-z][A-Za-z0-9_-]*)\\s*\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static NmuFile ParseFile(string path)
    {
        return Parse(File.ReadAllText(path), path);
    }

    public static NmuFile Parse(string source, string sourceName = "<memory>")
    {
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var firstContentLine = FindFirstContentLine(lines);
        if (firstContentLine < 0)
            throw new NmuParseException($"{sourceName}: file is empty.");

        var headerMatch = HeaderPattern.Match(lines[firstContentLine]);
        if (!headerMatch.Success)
            throw new NmuParseException($"{sourceName}:{firstContentLine + 1}: expected @nmu header.");

        var header = new NmuHeader(ParseAttributes(headerMatch.Groups["attributes"].Value, sourceName, firstContentLine + 1));
        var blocks = new List<NmuBlock>();
        var exports = new List<NmuExportDirective>();
        var lineIndex = firstContentLine + 1;

        while (lineIndex < lines.Length)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                lineIndex++;
                continue;
            }

            var exportMatch = ExportPattern.Match(line);
            if (exportMatch.Success)
            {
                exports.Add(new NmuExportDirective(exportMatch.Groups["target"].Value));
                lineIndex++;
                continue;
            }

            var blockMatch = BlockPattern.Match(line);
            if (!blockMatch.Success || line.Trim() == "::")
                throw new NmuParseException($"{sourceName}:{lineIndex + 1}: expected block header or @export directive.");

            var name = blockMatch.Groups["name"].Value.ToLowerInvariant();
            var attributes = ParseAttributes(blockMatch.Groups["attributes"].Value, sourceName, lineIndex + 1);
            var body = new StringBuilder();
            var bodyStart = lineIndex + 1;
            lineIndex++;
            var closed = false;

            while (lineIndex < lines.Length)
            {
                if (lines[lineIndex].Trim() == "::")
                {
                    closed = true;
                    break;
                }

                if (body.Length > 0)
                    body.Append('\n');
                body.Append(lines[lineIndex]);
                lineIndex++;
            }

            if (!closed)
                throw new NmuParseException($"{sourceName}:{bodyStart}: block '::{name}' is missing its closing '::'.");

            blocks.Add(CreateBlock(name, attributes, body.ToString(), sourceName, lineIndex + 1));
            lineIndex++;
        }

        return new NmuFile(header, blocks, exports);
    }

    private static NmuBlock CreateBlock(
        string name,
        IReadOnlyDictionary<string, string> attributes,
        string body,
        string sourceName,
        int line)
    {
        return name switch
        {
            "physics" or "cognition" or "design" => new NmuTierBlock(name, attributes, body),
            "mathpearl" => new NmuMathpearlBlock(attributes, body),
            "private" => new NmuPrivateBlock(attributes, body),
            _ => throw new NmuParseException($"{sourceName}:{line}: unsupported block '::{name}'.")
        };
    }

    private static Dictionary<string, string> ParseAttributes(string text, string sourceName, int line)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return attributes;

        foreach (var item in SplitAttributes(text, sourceName, line))
        {
            var separator = item.IndexOf('=');
            if (separator <= 0)
                throw new NmuParseException($"{sourceName}:{line}: attribute '{item.Trim()}' must use name=value.");

            var name = item[..separator].Trim();
            var value = item[(separator + 1)..].Trim();
            if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_-]*$"))
                throw new NmuParseException($"{sourceName}:{line}: invalid attribute name '{name}'.");
            if (value.Length < 2 || value[0] != '\"' || value[^1] != '\"')
                throw new NmuParseException($"{sourceName}:{line}: attribute '{name}' must use a quoted value.");

            attributes[name] = value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        return attributes;
    }

    private static IEnumerable<string> SplitAttributes(string text, string sourceName, int line)
    {
        var start = 0;
        var inString = false;
        var escaped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\"' && !escaped)
                inString = !inString;
            if (character == ',' && !inString)
            {
                yield return text[start..index];
                start = index + 1;
            }
            escaped = character == '\\' && !escaped;
            if (character != '\\')
                escaped = false;
        }

        if (inString)
            throw new NmuParseException($"{sourceName}:{line}: unterminated attribute string.");
        yield return text[start..];
    }

    private static int FindFirstContentLine(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]) && !lines[index].TrimStart().StartsWith('#'))
                return index;
        }

        return -1;
    }
}
