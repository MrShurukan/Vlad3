using Vlad3.Application.Errors;
using Vlad3.Application.Users;

namespace Vlad3.Application.Auth;

public sealed class AuthService
{
    private readonly IUserStore _userStore;
    private readonly PasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthService(IUserStore userStore, PasswordHasher passwordHasher, ITokenService tokenService)
    {
        _userStore = userStore;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw ServiceException.BadRequest("Username and password are required.");
        }

        var user = await _userStore.FindByUsernameAsync(username, cancellationToken);
        if (user is null || !_passwordHasher.Verify(user.PasswordHash, password))
        {
            throw ServiceException.Unauthorized("Invalid credentials.");
        }

        var token = _tokenService.CreateToken(user);
        return new AuthResult(user, token);
    }
}
