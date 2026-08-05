using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;

namespace Aletheia.RAGS.Application
{
    public sealed class ConfigurableTermNormalizer : ITermNormalizer
    {
        private readonly HashSet<string> _stopWords;
        private readonly HashSet<string> _exemptPhrases;

        public ConfigurableTermNormalizer(IOptions<TaxonomyOptions> opts, ILogger<ConfigurableTermNormalizer> logger)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            var options = opts.Value;
            _stopWords = new HashSet<string>(options.StopWords ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _exemptPhrases = LoadPhrasesFromTemplates(logger);
        }

        private static HashSet<string> LoadPhrasesFromTemplates(ILogger logger)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folder = LocateDocTemplatesFolder();
            if (folder is null)
            {
                logger?.LogWarning("Doc‑templates folder not found anywhere above the app base directory.");
                return set;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.md"))
            {
                var content = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(content, "{{(.*?)}}"))
                {
                    var phrase = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(phrase))
                        set.Add(phrase);
                }
                foreach (Match m in Regex.Matches(content, "^##\\s+(.+)$", RegexOptions.Multiline))
                {
                    var heading = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(heading))
                        set.Add(heading);
                }
            }
            logger?.LogInformation("Loaded {Count} phrase exemptions from doc‑templates.", set.Count);
            return set;
        }

        private static string? LocateDocTemplatesFolder()
        {
            // The templates folder lives at <repo root>/docs/doc-templates. The app base directory differs by
            // host (tests bin/Release/net10.0, API bin/Release/net10.0, or a container /app), so walk up the
            // directory tree until the folder is found.
            var candidate = Path.GetFullPath(AppContext.BaseDirectory);
            for (var depth = 0; depth < 12; depth++)
            {
                var folder = Path.Combine(candidate, "docs", "doc-templates");
                if (Directory.Exists(folder))
                {
                    return Path.GetFullPath(folder);
                }

                var parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) || parent == candidate)
                {
                    break;
                }

                candidate = parent;
            }

            return null;
        }

        public string? Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var trimmed = raw.Trim();

            // Preserve phrase exemptions
            if (_exemptPhrases.Contains(trimmed))
                return trimmed.ToLowerInvariant();

            var lower = trimmed.ToLowerInvariant();
            var cleaned = Regex.Replace(lower, "[^\\p{L}\\p{N}\\-]+", " ");
            var tokens = cleaned.Split(" ", StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1 && !_stopWords.Contains(t))
                .ToArray();
            if (tokens.Length == 0) return null;
            return string.Join(" ", tokens);
        }
    }
}
