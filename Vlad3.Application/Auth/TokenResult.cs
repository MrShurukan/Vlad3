namespace Vlad3.Application.Auth;

public sealed record TokenResult(string Token, DateTimeOffset ExpiresAt);
