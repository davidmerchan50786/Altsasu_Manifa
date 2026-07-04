#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Convai.Modules.LipSync.Editor
{
    internal static class ConvaiLipSyncMapImportParser
    {
        private static readonly Regex JsonKeyRegex = new("\"(?<key>[^\"]+)\"\\s*:", RegexOptions.Compiled);
        private static readonly string[] PairSeparators = { "=>", "->", ":", "=" };

        internal static bool TryParse(string rawText, out MappingImportData data, out string error)
        {
            data = null;
            error = null;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = "Import text is empty.";
                return false;
            }

            string trimmed = rawText.Trim();
            if (LooksLikeJson(trimmed))
            {
                if (TryParseJson(trimmed, out data, out string jsonError)) return true;

                if (TryParseJsonLikeObjectMappings(trimmed, out data, out string jsonObjectError)) return true;

                if (TryParseTuple(trimmed, out data, out string tupleError)) return true;

                if (TryParseSimplePairs(trimmed, out data, out string pairError)) return true;

                error =
                    $"{jsonError} Fallback JSON-object parse failed: {jsonObjectError} Fallback tuple parse failed: {tupleError} Fallback pair parse failed: {pairError}";
                return false;
            }

            if (TryParseTuple(trimmed, out data, out string tupleFirstError)) return true;

            if (TryParseSimplePairs(trimmed, out data, out string pairFirstError)) return true;

            if (TryParseJson(trimmed, out data, out string jsonFallbackError)) return true;

            if (TryParseJsonLikeObjectMappings(trimmed, out data, out string jsonObjectFallbackError)) return true;

            error =
                $"{tupleFirstError} Fallback pair parse failed: {pairFirstError} Fallback JSON parse failed: {jsonFallbackError} Fallback JSON-object parse failed: {jsonObjectFallbackError}";
            return false;
        }

        private static bool LooksLikeJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            char first = text[0];
            return first == '{' || first == '[';
        }

        private static bool TryParseJson(string rawJson, out MappingImportData data, out string error)
        {
            data = new MappingImportData();
            error = null;

            try
            {
                string normalizedJson = NormalizeJsonKeys(rawJson);
                string workingJson = normalizedJson.TrimStart();
                if (workingJson.StartsWith("[", StringComparison.Ordinal))
                    workingJson = "{\"mappings\":" + workingJson + "}";

                bool hasTargetProfile = ContainsJsonKey(workingJson, "targetProfileId");
                bool hasDescription = ContainsJsonKey(workingJson, "description");
                bool hasGlobalMultiplier = ContainsJsonKey(workingJson, "globalMultiplier");
                bool hasGlobalOffset = ContainsJsonKey(workingJson, "globalOffset");
                bool hasAllowUnmapped = ContainsJsonKey(workingJson, "allowUnmappedPassthrough");

                var root = JsonUtility.FromJson<JsonMappingRoot>(workingJson);
                if (root == null)
                {
                    error = "JSON parse failed: root object is null.";
                    return false;
                }

                if (root.mappings == null)
                {
                    error = "JSON parse failed: expected a 'mappings' array.";
                    return false;
                }

                if (hasTargetProfile && !string.IsNullOrWhiteSpace(root.targetProfileId))
                    data.TargetProfileId = root.targetProfileId.Trim();

                if (hasDescription)
                {
                    data.HasDescription = true;
                    data.Description = root.description ?? string.Empty;
                }

                if (hasGlobalMultiplier) data.GlobalMultiplier = root.globalMultiplier;

                if (hasGlobalOffset) data.GlobalOffset = root.globalOffset;

                if (hasAllowUnmapped) data.AllowUnmappedPassthrough = root.allowUnmappedPassthrough;

                for (int i = 0; i < root.mappings.Length; i++)
                {
                    JsonMappingEntry rawEntry = root.mappings[i];
                    if (rawEntry == null)
                    {
                        data.Warnings.Add($"Entry {i} is null and was skipped.");
                        continue;
                    }

                    var parsed = new ImportedEntry
                    {
                        SourceBlendshape = (rawEntry.sourceBlendshape ?? string.Empty).Trim(),
                        Multiplier = rawEntry.multiplier,
                        Offset = rawEntry.offset,
                        Enabled = rawEntry.enabled,
                        UseOverrideValue = rawEntry.useOverrideValue,
                        OverrideValue = rawEntry.overrideValue,
                        IgnoreGlobalModifiers = rawEntry.ignoreGlobalModifiers,
                        ClampMinValue = rawEntry.clampMinValue,
                        ClampMaxValue = rawEntry.clampMaxValue
                    };

                    if (rawEntry.targetNames != null && rawEntry.targetNames.Length > 0)
                    {
                        for (int j = 0; j < rawEntry.targetNames.Length; j++)
                        {
                            string target = (rawEntry.targetNames[j] ?? string.Empty).Trim();
                            if (!string.IsNullOrEmpty(target)) parsed.TargetNames.Add(target);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(rawEntry.targetName))
                        parsed.TargetNames.Add(rawEntry.targetName.Trim());

                    if (string.IsNullOrWhiteSpace(parsed.SourceBlendshape))
                    {
                        data.Warnings.Add($"Entry {i} has empty sourceBlendshape and was skipped.");
                        continue;
                    }

                    DeduplicateTargets(parsed.TargetNames);
                    data.Entries.Add(parsed);
                }

                if (data.Entries.Count == 0)
                {
                    error = "JSON parse failed: no valid mapping entries found.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"JSON parse failed: {ex.Message}";
                return false;
            }
        }

        private static bool TryParseTuple(string rawText, out MappingImportData data, out string error)
        {
            data = new MappingImportData();
            error = null;

            string text = rawText.Trim();
            if (!text.StartsWith("(", StringComparison.Ordinal) ||
                text.IndexOf("TargetNames", StringComparison.OrdinalIgnoreCase) < 0)
            {
                error = "Tuple parse failed: input does not look like tuple mapping format.";
                return false;
            }

            string flattened = TrimBalancedWrapping(text);
            if (string.IsNullOrWhiteSpace(flattened))
            {
                error = "Tuple parse failed: empty tuple payload.";
                return false;
            }

            List<string> tokens = SplitTopLevel(flattened, ',');
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i].Trim();
                if (string.IsNullOrEmpty(token)) continue;

                string entryPayload = TrimBalancedWrapping(token);
                if (!TryParseTupleEntry(entryPayload, out ImportedEntry entry, out string entryError))
                {
                    data.Warnings.Add($"Entry {i} skipped: {entryError}");
                    continue;
                }

                data.Entries.Add(entry);
            }

            if (data.Entries.Count == 0)
            {
                error = "Tuple parse failed: no valid entries found.";
                return false;
            }

            return true;
        }

        private static bool TryParseJsonLikeObjectMappings(string rawText, out MappingImportData data, out string error)
        {
            data = new MappingImportData();
            error = null;

            string text = rawText.Trim();
            if (!HasBalancedOuterWrap(text, '{', '}'))
            {
                error = "JSON-object parse failed: input is not a balanced object.";
                return false;
            }

            if (!TryParseMappingObject(text, data, true, out error)) return false;

            if (data.Entries.Count == 0)
            {
                error = "JSON-object parse failed: no mapping entries found.";
                return false;
            }

            return true;
        }

        private static bool TryParseSimplePairs(string rawText, out MappingImportData data, out string error)
        {
            data = new MappingImportData();
            error = null;

            string text = rawText.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Pair parse failed: input is empty.";
                return false;
            }

            if (HasBalancedOuterWrap(text, '{', '}'))
            {
                error = "Pair parse failed: looks like a JSON object.";
                return false;
            }

            var segments = new List<string>();
            if (text.Contains("\n") || text.Contains("\r"))
            {
                string[] lines = text.Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++) segments.Add(lines[i]);
            }
            else
            {
                List<string> splitByComma = SplitTopLevel(text, ',');
                if (splitByComma.Count > 1)
                    segments.AddRange(splitByComma);
                else
                {
                    List<string> splitBySemicolon = SplitTopLevel(text, ';');
                    if (splitBySemicolon.Count > 1)
                        segments.AddRange(splitBySemicolon);
                    else
                        segments.Add(text);
                }
            }

            int validCount = 0;
            bool foundSeparator = false;
            for (int i = 0; i < segments.Count; i++)
            {
                string line = segments[i].Trim().TrimEnd(',');
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("//", StringComparison.Ordinal) ||
                    line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (!TrySplitPair(line, out string sourceToken, out string valueToken))
                {
                    data.Warnings.Add($"Line {i + 1} skipped: no mapping separator found.");
                    continue;
                }

                foundSeparator = true;
                string source = ParseTokenString(sourceToken).Trim();
                if (string.IsNullOrWhiteSpace(source))
                {
                    data.Warnings.Add($"Line {i + 1} skipped: empty source blendshape.");
                    continue;
                }

                var entry = new ImportedEntry { SourceBlendshape = source };

                string value = valueToken.Trim();
                if (HasBalancedOuterWrap(value, '{', '}'))
                {
                    if (!TryApplyInlineMappingObject(value, entry, out string inlineError))
                    {
                        data.Warnings.Add($"Line {i + 1} skipped: {inlineError}");
                        continue;
                    }
                }
                else
                    entry.TargetNames = ParseSimpleTargets(value);

                DeduplicateTargets(entry.TargetNames);
                data.Entries.Add(entry);
                validCount++;
            }

            if (!foundSeparator)
            {
                error = "Pair parse failed: no supported separator found (=>, ->, :, =).";
                return false;
            }

            if (validCount == 0)
            {
                error = "Pair parse failed: no valid entries found.";
                return false;
            }

            return true;
        }

        private static bool TryParseTupleEntry(string tupleEntry, out ImportedEntry entry, out string error)
        {
            entry = new ImportedEntry();
            error = null;

            int firstComma = IndexOfTopLevel(tupleEntry, ',');
            if (firstComma < 0)
            {
                error = "Missing source/body separator.";
                return false;
            }

            string sourceToken = tupleEntry.Substring(0, firstComma).Trim();
            string bodyToken = tupleEntry.Substring(firstComma + 1).Trim();
            string source = ParseTokenString(sourceToken);
            if (string.IsNullOrWhiteSpace(source))
            {
                error = "Source blendshape is empty.";
                return false;
            }

            entry.SourceBlendshape = source;
            bodyToken = TrimBalancedWrapping(bodyToken);

            List<string> keyValues = SplitTopLevel(bodyToken, ',');
            bool hasUseOverrideFlag = false;
            bool hasOverrideValue = false;
            bool overrideIsNegativeSentinel = false;
            float parsedOverride = 0f;

            for (int i = 0; i < keyValues.Count; i++)
            {
                string keyValue = keyValues[i].Trim();
                if (string.IsNullOrEmpty(keyValue)) continue;

                int eq = IndexOfTopLevel(keyValue, '=');
                if (eq < 0) continue;

                string key = keyValue.Substring(0, eq).Trim();
                string value = keyValue.Substring(eq + 1).Trim();
                string keyNorm = key.Replace("_", string.Empty).Trim().ToLowerInvariant();

                switch (keyNorm)
                {
                    case "targetnames":
                        {
                            entry.TargetNames = ParseTargetNames(value);
                            break;
                        }
                    case "enabled":
                        {
                            if (TryParseBool(value, out bool enabled)) entry.Enabled = enabled;

                            break;
                        }
                    case "multiplier":
                        {
                            if (TryParseFloat(value, out float parsed)) entry.Multiplier = parsed;

                            break;
                        }
                    case "offset":
                        {
                            if (TryParseFloat(value, out float parsed)) entry.Offset = parsed;

                            break;
                        }
                    case "useoverridevalue":
                        {
                            if (TryParseBool(value, out bool useOverride))
                            {
                                entry.UseOverrideValue = useOverride;
                                hasUseOverrideFlag = true;
                            }

                            break;
                        }
                    case "overridevalue":
                        {
                            if (TryParseFloat(value, out float parsed))
                            {
                                hasOverrideValue = true;
                                parsedOverride = parsed;
                                overrideIsNegativeSentinel = parsed < 0f;
                            }

                            break;
                        }
                    case "ignoreglobalmodifiers":
                        {
                            if (TryParseBool(value, out bool ignoreGlobal)) entry.IgnoreGlobalModifiers = ignoreGlobal;

                            break;
                        }
                    case "clampminvalue":
                        {
                            if (TryParseFloat(value, out float parsed)) entry.ClampMinValue = parsed;

                            break;
                        }
                    case "clampmaxvalue":
                        {
                            if (TryParseFloat(value, out float parsed)) entry.ClampMaxValue = parsed;

                            break;
                        }
                }
            }

            if (hasOverrideValue)
            {
                if (!hasUseOverrideFlag) entry.UseOverrideValue = !overrideIsNegativeSentinel;

                entry.OverrideValue = overrideIsNegativeSentinel ? 0f : parsedOverride;
            }

            DeduplicateTargets(entry.TargetNames);
            return true;
        }

        private static string NormalizeJsonKeys(string json)
        {
            return JsonKeyRegex.Replace(json, match =>
            {
                string key = match.Groups["key"].Value;
                string normalized = NormalizeJsonKey(key);
                return "\"" + normalized + "\":";
            });
        }

        private static string NormalizeJsonKey(string key)
        {
            string normalized = key.Trim().Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            switch (normalized)
            {
                case "targetprofileid":
                case "profileid":
                case "profile":
                    return "targetProfileId";
                case "description":
                    return "description";
                case "globalmultiplier":
                    return "globalMultiplier";
                case "globaloffset":
                    return "globalOffset";
                case "allowunmappedpassthrough":
                case "allowunmapped":
                    return "allowUnmappedPassthrough";
                case "mappings":
                case "mapping":
                case "entries":
                    return "mappings";
                case "sourceblendshape":
                case "source":
                    return "sourceBlendshape";
                case "targetnames":
                case "targets":
                    return "targetNames";
                case "targetname":
                case "target":
                    return "targetName";
                case "multiplier":
                    return "multiplier";
                case "offset":
                    return "offset";
                case "enabled":
                    return "enabled";
                case "useoverridevalue":
                    return "useOverrideValue";
                case "overridevalue":
                    return "overrideValue";
                case "ignoreglobalmodifiers":
                    return "ignoreGlobalModifiers";
                case "clampminvalue":
                    return "clampMinValue";
                case "clampmaxvalue":
                    return "clampMaxValue";
                default:
                    return key;
            }
        }

        private static bool TryParseMappingObject(string objectToken, MappingImportData data, bool allowMetadata,
            out string error)
        {
            error = null;
            string body = objectToken.Substring(1, objectToken.Length - 2).Trim();
            if (string.IsNullOrWhiteSpace(body)) return true;

            List<string> keyValues = SplitTopLevel(body, ',');
            for (int i = 0; i < keyValues.Count; i++)
            {
                string pair = keyValues[i].Trim();
                if (string.IsNullOrWhiteSpace(pair)) continue;

                if (!TrySplitPair(pair, out string keyToken, out string valueToken))
                {
                    data.Warnings.Add($"Object entry {i} skipped: invalid key/value pair.");
                    continue;
                }

                string rawKey = ParseTokenString(keyToken).Trim();
                if (string.IsNullOrWhiteSpace(rawKey))
                {
                    data.Warnings.Add($"Object entry {i} skipped: empty key.");
                    continue;
                }

                string normalizedKey = NormalizeJsonKey(rawKey);
                string value = valueToken.Trim();

                if (allowMetadata)
                {
                    switch (normalizedKey)
                    {
                        case "targetProfileId":
                            data.TargetProfileId = ParseTokenString(value).Trim();
                            continue;
                        case "description":
                            data.HasDescription = true;
                            data.Description = ParseTokenString(value);
                            continue;
                        case "globalMultiplier":
                            if (TryParseFloat(value, out float globalMult)) data.GlobalMultiplier = globalMult;
                            continue;
                        case "globalOffset":
                            if (TryParseFloat(value, out float globalOffset)) data.GlobalOffset = globalOffset;
                            continue;
                        case "allowUnmappedPassthrough":
                            if (TryParseBool(value, out bool allowUnmapped))
                                data.AllowUnmappedPassthrough = allowUnmapped;
                            continue;
                        case "mappings":
                            if (HasBalancedOuterWrap(value, '{', '}'))
                            {
                                if (!TryParseMappingObject(value, data, false, out string nestedError))
                                {
                                    error = nestedError ?? "JSON-object parse failed in nested mappings object.";
                                    return false;
                                }
                            }
                            else if (HasBalancedOuterWrap(value, '[', ']'))
                            {
                                error =
                                    "JSON-object parse failed: mappings array should be imported via JSON array schema.";
                                return false;
                            }

                            continue;
                    }
                }

                var entry = new ImportedEntry { SourceBlendshape = rawKey };

                if (HasBalancedOuterWrap(value, '{', '}'))
                {
                    if (!TryApplyInlineMappingObject(value, entry, out string inlineError))
                    {
                        data.Warnings.Add($"Source '{rawKey}' skipped: {inlineError}");
                        continue;
                    }
                }
                else
                    entry.TargetNames = ParseSimpleTargets(value);

                DeduplicateTargets(entry.TargetNames);
                data.Entries.Add(entry);
            }

            return true;
        }

        private static bool TryApplyInlineMappingObject(string objectToken, ImportedEntry entry, out string error)
        {
            error = null;
            string body = objectToken.Substring(1, objectToken.Length - 2).Trim();
            if (string.IsNullOrWhiteSpace(body)) return true;

            List<string> keyValues = SplitTopLevel(body, ',');
            bool hasUseOverrideFlag = false;
            bool hasOverrideValue = false;
            bool overrideIsNegativeSentinel = false;
            float parsedOverride = 0f;

            for (int i = 0; i < keyValues.Count; i++)
            {
                string pair = keyValues[i].Trim();
                if (string.IsNullOrWhiteSpace(pair)) continue;

                if (!TrySplitPair(pair, out string keyToken, out string valueToken)) continue;

                string keyNorm = NormalizeJsonKey(ParseTokenString(keyToken));
                string value = valueToken.Trim();
                switch (keyNorm)
                {
                    case "targetNames":
                        entry.TargetNames = ParseSimpleTargets(value);
                        break;
                    case "targetName":
                        entry.TargetNames = ParseSimpleTargets(value);
                        break;
                    case "enabled":
                        if (TryParseBool(value, out bool enabled)) entry.Enabled = enabled;
                        break;
                    case "multiplier":
                        if (TryParseFloat(value, out float mult)) entry.Multiplier = mult;
                        break;
                    case "offset":
                        if (TryParseFloat(value, out float offset)) entry.Offset = offset;
                        break;
                    case "useOverrideValue":
                        if (TryParseBool(value, out bool useOverride))
                        {
                            entry.UseOverrideValue = useOverride;
                            hasUseOverrideFlag = true;
                        }

                        break;
                    case "overrideValue":
                        if (TryParseFloat(value, out float ov))
                        {
                            hasOverrideValue = true;
                            parsedOverride = ov;
                            overrideIsNegativeSentinel = ov < 0f;
                        }

                        break;
                    case "ignoreGlobalModifiers":
                        if (TryParseBool(value, out bool ignoreGlobal)) entry.IgnoreGlobalModifiers = ignoreGlobal;
                        break;
                    case "clampMinValue":
                        if (TryParseFloat(value, out float clampMin)) entry.ClampMinValue = clampMin;
                        break;
                    case "clampMaxValue":
                        if (TryParseFloat(value, out float clampMax)) entry.ClampMaxValue = clampMax;
                        break;
                }
            }

            if (hasOverrideValue)
            {
                if (!hasUseOverrideFlag) entry.UseOverrideValue = !overrideIsNegativeSentinel;

                entry.OverrideValue = overrideIsNegativeSentinel ? 0f : parsedOverride;
            }

            return true;
        }

        private static bool ContainsJsonKey(string json, string key) =>
            json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase) >= 0;

        private static string TrimBalancedWrapping(string text)
        {
            string result = text.Trim();
            while (HasBalancedOuterWrap(result, '(', ')')) result = result.Substring(1, result.Length - 2).Trim();

            return result;
        }

        private static bool HasBalancedOuterWrap(string text, char open, char close)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2) return false;

            if (text[0] != open || text[text.Length - 1] != close) return false;

            int depth = 0;
            bool inQuote = false;
            char quoteChar = '\0';

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuote)
                {
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        continue;
                    }

                    if (c == quoteChar) inQuote = false;

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quoteChar = c;
                    continue;
                }

                if (c == open)
                {
                    depth++;
                    continue;
                }

                if (c != close) continue;

                depth--;
                if (depth < 0) return false;

                if (depth == 0 && i < text.Length - 1) return false;
            }

            return depth == 0;
        }

        private static List<string> SplitTopLevel(string input, char separator)
        {
            var parts = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return parts;

            int depthParen = 0;
            int depthBracket = 0;
            int depthBrace = 0;
            bool inQuote = false;
            char quoteChar = '\0';
            int lastIndex = 0;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (inQuote)
                {
                    if (c == '\\' && i + 1 < input.Length)
                    {
                        i++;
                        continue;
                    }

                    if (c == quoteChar) inQuote = false;

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quoteChar = c;
                    continue;
                }

                switch (c)
                {
                    case '(':
                        depthParen++;
                        break;
                    case ')':
                        depthParen = Mathf.Max(0, depthParen - 1);
                        break;
                    case '[':
                        depthBracket++;
                        break;
                    case ']':
                        depthBracket = Mathf.Max(0, depthBracket - 1);
                        break;
                    case '{':
                        depthBrace++;
                        break;
                    case '}':
                        depthBrace = Mathf.Max(0, depthBrace - 1);
                        break;
                }

                if (c == separator && depthParen == 0 && depthBracket == 0 && depthBrace == 0)
                {
                    parts.Add(input.Substring(lastIndex, i - lastIndex));
                    lastIndex = i + 1;
                }
            }

            if (lastIndex <= input.Length) parts.Add(input.Substring(lastIndex));

            return parts;
        }

        private static int IndexOfTopLevel(string input, char separator)
        {
            int depthParen = 0;
            int depthBracket = 0;
            int depthBrace = 0;
            bool inQuote = false;
            char quoteChar = '\0';

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (inQuote)
                {
                    if (c == '\\' && i + 1 < input.Length)
                    {
                        i++;
                        continue;
                    }

                    if (c == quoteChar) inQuote = false;

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quoteChar = c;
                    continue;
                }

                switch (c)
                {
                    case '(':
                        depthParen++;
                        break;
                    case ')':
                        depthParen = Mathf.Max(0, depthParen - 1);
                        break;
                    case '[':
                        depthBracket++;
                        break;
                    case ']':
                        depthBracket = Mathf.Max(0, depthBracket - 1);
                        break;
                    case '{':
                        depthBrace++;
                        break;
                    case '}':
                        depthBrace = Mathf.Max(0, depthBrace - 1);
                        break;
                }

                if (c == separator && depthParen == 0 && depthBracket == 0 && depthBrace == 0) return i;
            }

            return -1;
        }

        private static int IndexOfTopLevelToken(string input, string token)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(token)) return -1;

            int depthParen = 0;
            int depthBracket = 0;
            int depthBrace = 0;
            bool inQuote = false;
            char quoteChar = '\0';

            for (int i = 0; i <= input.Length - token.Length; i++)
            {
                char c = input[i];
                if (inQuote)
                {
                    if (c == '\\' && i + 1 < input.Length)
                    {
                        i++;
                        continue;
                    }

                    if (c == quoteChar) inQuote = false;

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quoteChar = c;
                    continue;
                }

                switch (c)
                {
                    case '(':
                        depthParen++;
                        break;
                    case ')':
                        depthParen = Mathf.Max(0, depthParen - 1);
                        break;
                    case '[':
                        depthBracket++;
                        break;
                    case ']':
                        depthBracket = Mathf.Max(0, depthBracket - 1);
                        break;
                    case '{':
                        depthBrace++;
                        break;
                    case '}':
                        depthBrace = Mathf.Max(0, depthBrace - 1);
                        break;
                }

                if (depthParen != 0 || depthBracket != 0 || depthBrace != 0) continue;

                if (string.CompareOrdinal(input, i, token, 0, token.Length) == 0) return i;
            }

            return -1;
        }

        private static bool TrySplitPair(string input, out string left, out string right)
        {
            left = string.Empty;
            right = string.Empty;

            if (string.IsNullOrWhiteSpace(input)) return false;

            for (int i = 0; i < PairSeparators.Length; i++)
            {
                string separator = PairSeparators[i];
                int index = IndexOfTopLevelToken(input, separator);
                if (index < 0) continue;

                left = input.Substring(0, index).Trim();
                right = input.Substring(index + separator.Length).Trim();
                return true;
            }

            return false;
        }

        private static List<string> ParseTargetNames(string valueToken)
        {
            string token = valueToken.Trim();
            if (HasBalancedOuterWrap(token, '(', ')')) token = token.Substring(1, token.Length - 2).Trim();

            var targets = new List<string>();
            if (string.IsNullOrWhiteSpace(token)) return targets;

            List<string> split = SplitTopLevel(token, ',');
            for (int i = 0; i < split.Count; i++)
            {
                string parsed = ParseTokenString(split[i]);
                if (!string.IsNullOrWhiteSpace(parsed)) targets.Add(parsed.Trim());
            }

            return targets;
        }

        private static List<string> ParseSimpleTargets(string valueToken)
        {
            string token = valueToken.Trim();
            if (HasBalancedOuterWrap(token, '[', ']') || HasBalancedOuterWrap(token, '(', ')'))
                token = token.Substring(1, token.Length - 2).Trim();

            if (string.IsNullOrWhiteSpace(token)) return new List<string>();

            List<string> parts = SplitTopLevel(token, ',');
            if (parts.Count <= 1 && token.Contains("|"))
                parts = new List<string>(token.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));

            var targets = new List<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                string parsed = ParseTokenString(parts[i]).Trim();
                if (!string.IsNullOrWhiteSpace(parsed)) targets.Add(parsed);
            }

            return targets;
        }

        private static string ParseTokenString(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;

            string trimmed = token.Trim();
            if (trimmed.Length >= 2)
            {
                char first = trimmed[0];
                char last = trimmed[trimmed.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    return trimmed.Substring(1, trimmed.Length - 2)
                        .Replace("\\\"", "\"")
                        .Replace("\\'", "'")
                        .Replace("\\\\", "\\");
                }
            }

            return trimmed;
        }

        private static bool TryParseFloat(string token, out float value)
        {
            string parsed = ParseTokenString(token);
            if (float.TryParse(parsed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;

            return float.TryParse(parsed, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseBool(string token, out bool value)
        {
            string parsed = ParseTokenString(token).Trim();
            if (bool.TryParse(parsed, out value)) return true;

            if (parsed == "1")
            {
                value = true;
                return true;
            }

            if (parsed == "0")
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        private static void DeduplicateTargets(List<string> targetNames)
        {
            if (targetNames == null || targetNames.Count <= 1) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>(targetNames.Count);
            for (int i = 0; i < targetNames.Count; i++)
            {
                string value = targetNames[i];
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (seen.Add(value)) ordered.Add(value);
            }

            targetNames.Clear();
            targetNames.AddRange(ordered);
        }

        internal sealed class MappingImportData
        {
            public readonly List<ImportedEntry> Entries = new();
            public readonly List<string> Warnings = new();
            public bool? AllowUnmappedPassthrough;
            public string Description;
            public float? GlobalMultiplier;
            public float? GlobalOffset;
            public bool HasDescription;
            public string TargetProfileId;
        }

        internal sealed class ImportedEntry
        {
            public float ClampMaxValue = 1f;
            public float ClampMinValue;
            public bool Enabled = true;
            public bool IgnoreGlobalModifiers;
            public float Multiplier = 1f;
            public float Offset;
            public float OverrideValue;
            public string SourceBlendshape;
            public List<string> TargetNames = new();
            public bool UseOverrideValue;
        }

        [Serializable]
        private sealed class JsonMappingRoot
        {
            public string targetProfileId;
            public string description;
            public float globalMultiplier = 1f;
            public float globalOffset;
            public bool allowUnmappedPassthrough = true;
            public JsonMappingEntry[] mappings;
        }

        [Serializable]
        private sealed class JsonMappingEntry
        {
            public string sourceBlendshape;
            public string targetName;
            public string[] targetNames;
            public float multiplier = 1f;
            public float offset;
            public bool enabled = true;
            public bool useOverrideValue;
            public float overrideValue;
            public bool ignoreGlobalModifiers;
            public float clampMinValue;
            public float clampMaxValue = 1f;
        }
    }
}
#endif
