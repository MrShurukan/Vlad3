using System.Text.Json;
using Vlad3.Application.Storage;
using Vlad3.Core.Models;

namespace Vlad3.Application.Bots;

public sealed class BotConfigurationStore : IBotConfigurationStore
{
    private readonly StoragePathResolver _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public BotConfigurationStore(StoragePathResolver paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<BotConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAllAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BotConfiguration?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var configs = await ReadAllAsync(cancellationToken);
            return configs.FirstOrDefault(config => config.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(BotConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var configs = await ReadAllAsync(cancellationToken);
            configs.Add(configuration);
            await WriteAllAsync(configs, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(BotConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var configs = await ReadAllAsync(cancellationToken);
            var index = configs.FindIndex(existing => existing.Id == configuration.Id);
            if (index >= 0)
            {
                configs[index] = configuration;
                await WriteAllAsync(configs, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var configs = await ReadAllAsync(cancellationToken);
            configs.RemoveAll(config => config.Id == id);
            await WriteAllAsync(configs, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<BotConfiguration>> ReadAllAsync(CancellationToken cancellationToken)
    {
        EnsureStorageDirectory();
        if (!File.Exists(_paths.BotsFile))
        {
            return new List<BotConfiguration>();
        }

        var json = await File.ReadAllTextAsync(_paths.BotsFile, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<BotConfiguration>();
        }

        var configs = JsonSerializer.Deserialize<List<BotConfiguration>>(json, _jsonOptions) ?? new List<BotConfiguration>();
        return configs
            .Select(config => config.Settings is null
                ? config with { Settings = new Dictionary<string, string>() }
                : config)
            .ToList();
    }

    private async Task WriteAllAsync(List<BotConfiguration> configurations, CancellationToken cancellationToken)
    {
        EnsureStorageDirectory();
        var json = JsonSerializer.Serialize(configurations, _jsonOptions);
        await File.WriteAllTextAsync(_paths.BotsFile, json, cancellationToken);
    }

    private void EnsureStorageDirectory()
    {
        Directory.CreateDirectory(_paths.RootPath);
    }
}
