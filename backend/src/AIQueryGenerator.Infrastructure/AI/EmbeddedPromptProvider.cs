using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using AIQueryGenerator.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace AIQueryGenerator.Infrastructure.AI;

/// <summary>
/// Loads versioned system prompts embedded as "AI/Prompts/{name}.v{N}.md". The highest version is used unless
/// configuration pins one (e.g. "Ai:PromptVersions:sql-generation" = "v1").
/// </summary>
public sealed partial class EmbeddedPromptProvider(IConfiguration configuration) : IPromptProvider
{
    private static readonly Lazy<IReadOnlyList<PromptTemplate>> All = new(Load);
    private readonly ConcurrentDictionary<string, PromptTemplate> _cache = new();

    public PromptTemplate Get(string name) => _cache.GetOrAdd(name, n =>
    {
        var pinned = configuration[$"Ai:PromptVersions:{n}"];
        var candidates = All.Value.Where(p => p.Name == n).OrderByDescending(p => int.Parse(p.Version[1..])).ToList();
        return (pinned is null ? candidates.FirstOrDefault() : candidates.FirstOrDefault(p => p.Version == pinned))
               ?? throw new InvalidOperationException($"Prompt '{n}' (version {pinned ?? "latest"}) was not found.");
    });

    private static IReadOnlyList<PromptTemplate> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var list = new List<PromptTemplate>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var match = ResourcePattern().Match(resource);
            if (!match.Success) continue;
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            list.Add(new PromptTemplate(match.Groups["name"].Value, match.Groups["version"].Value, reader.ReadToEnd().Trim()));
        }
        return list;
    }

    [GeneratedRegex(@"\.Prompts\.(?<name>[a-z\-]+)\.(?<version>v\d+)\.md$")]
    private static partial Regex ResourcePattern();
}
