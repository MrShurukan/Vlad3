using System.Text.Json;
using Vlad3.Application.Storage;

namespace Vlad3.Application.Users;

public sealed class UserStore : IUserStore
{
    private readonly StoragePathResolver _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public UserStore(StoragePathResolver paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<UserRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadAllAsync(cancellationToken);
            return users;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadAllAsync(cancellationToken);
            return users.FirstOrDefault(user => string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadAllAsync(cancellationToken);
            return users.FirstOrDefault(user => user.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadAllAsync(cancellationToken);
            users.Add(user);
            await WriteAllAsync(users, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadAllAsync(cancellationToken);
            var index = users.FindIndex(existing => existing.Id == user.Id);
            if (index >= 0)
            {
                users[index] = user;
                await WriteAllAsync(users, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<UserRecord>> ReadAllAsync(CancellationToken cancellationToken)
    {
        EnsureStorageDirectory();
        if (!File.Exists(_paths.UsersFile))
        {
            return new List<UserRecord>();
        }

        var json = await File.ReadAllTextAsync(_paths.UsersFile, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<UserRecord>();
        }

        return JsonSerializer.Deserialize<List<UserRecord>>(json, _jsonOptions) ?? new List<UserRecord>();
    }

    private async Task WriteAllAsync(List<UserRecord> users, CancellationToken cancellationToken)
    {
        EnsureStorageDirectory();
        var json = JsonSerializer.Serialize(users, _jsonOptions);
        await File.WriteAllTextAsync(_paths.UsersFile, json, cancellationToken);
    }

    private void EnsureStorageDirectory()
    {
        Directory.CreateDirectory(_paths.RootPath);
    }
}
