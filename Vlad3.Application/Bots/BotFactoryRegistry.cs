using Vlad3.Application.Errors;
using Vlad3.Core.Abstractions;
using Vlad3.Core.Models;

namespace Vlad3.Application.Bots;

public sealed class BotFactoryRegistry
{
    private readonly Dictionary<string, IAudioBotFactory> _factories;

    public BotFactoryRegistry(IEnumerable<IAudioBotFactory> factories)
    {
        _factories = factories.ToDictionary(factory => factory.Type, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<BotTypeInfo> GetTypes()
        => _factories.Values
            .Select(factory => new BotTypeInfo(factory.Type, factory.DisplayName))
            .OrderBy(info => info.DisplayName)
            .ToList();

    public IAudioBotFactory GetFactory(string type)
    {
        if (!_factories.TryGetValue(type, out var factory))
        {
            throw ServiceException.NotFound($"Bot type '{type}' is not registered.");
        }

        return factory;
    }
}
