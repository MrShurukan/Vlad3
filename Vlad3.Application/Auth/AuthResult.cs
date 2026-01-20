using Vlad3.Application.Users;

namespace Vlad3.Application.Auth;

public sealed record AuthResult(UserRecord User, TokenResult Token);
