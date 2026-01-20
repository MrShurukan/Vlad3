namespace Vlad3.WebControl.Models;

public sealed record CreateUserRequest(string Username, string Password, string Role);

public sealed record UserDto(string Id, string Username, string Role);
