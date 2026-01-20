namespace Vlad3.WebControl.Models;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    string UserId,
    string Username,
    string Role);
