namespace Vlad3.Core.Models;

public sealed record BotConfiguration(
    string Id,
    string Type,
    string Label,
    string ApiKey,
    IReadOnlyDictionary<string, string> Settings);
