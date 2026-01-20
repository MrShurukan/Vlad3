namespace Vlad3.Application.Users;

public sealed record UserRecord(
    string Id,
    string Username,
    string Role,
    string PasswordHash);
