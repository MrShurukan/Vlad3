using Vlad3.Application.Auth;
using Vlad3.Application.Errors;
using Vlad3.Core.Models;

namespace Vlad3.Application.Users;

public sealed class UserService
{
    private readonly IUserStore _userStore;
    private readonly PasswordHasher _passwordHasher;

    public UserService(IUserStore userStore, PasswordHasher passwordHasher)
    {
        _userStore = userStore;
        _passwordHasher = passwordHasher;
    }

    public Task<IReadOnlyList<UserRecord>> GetAllAsync(CancellationToken cancellationToken = default)
        => _userStore.GetAllAsync(cancellationToken);

    public async Task<UserRecord> CreateUserAsync(string username, string password, string role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw ServiceException.BadRequest("Username and password are required.");
        }

        var normalizedRole = role?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsRoleAllowed(normalizedRole))
        {
            throw ServiceException.BadRequest("Unsupported role.");
        }

        var existing = await _userStore.FindByUsernameAsync(username, cancellationToken);
        if (existing is not null)
        {
            throw ServiceException.Conflict("User already exists.");
        }

        var user = new UserRecord(
            Guid.NewGuid().ToString("N"),
            username.Trim(),
            normalizedRole,
            _passwordHasher.HashPassword(password));

        await _userStore.AddAsync(user, cancellationToken);
        return user;
    }

    private static bool IsRoleAllowed(string role)
        => string.Equals(role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, UserRoles.Operator, StringComparison.OrdinalIgnoreCase);
}
