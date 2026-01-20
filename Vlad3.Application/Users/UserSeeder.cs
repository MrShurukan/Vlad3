using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vlad3.Application.Auth;
using Vlad3.Application.Options;
using Vlad3.Core.Models;

namespace Vlad3.Application.Users;

public sealed class UserSeeder
{
    private readonly IUserStore _userStore;
    private readonly PasswordHasher _passwordHasher;
    private readonly AdminSeedOptions _seedOptions;
    private readonly ILogger<UserSeeder> _logger;

    public UserSeeder(
        IUserStore userStore,
        PasswordHasher passwordHasher,
        IOptions<AdminSeedOptions> seedOptions,
        ILogger<UserSeeder> logger)
    {
        _userStore = userStore;
        _passwordHasher = passwordHasher;
        _seedOptions = seedOptions.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingUsers = await _userStore.GetAllAsync(cancellationToken);
        if (existingUsers.Count > 0)
        {
            return;
        }

        var username = string.IsNullOrWhiteSpace(_seedOptions.Username) ? "admin" : _seedOptions.Username.Trim();
        var password = string.IsNullOrWhiteSpace(_seedOptions.Password) ? "admin" : _seedOptions.Password;

        var user = new UserRecord(
            Guid.NewGuid().ToString("N"),
            username,
            UserRoles.Admin,
            _passwordHasher.HashPassword(password));

        await _userStore.AddAsync(user, cancellationToken);
        _logger.LogWarning("Seeded default admin user '{Username}'. Please change the password.", username);
    }
}
