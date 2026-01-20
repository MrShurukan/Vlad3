using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vlad3.Application.Users;
using Vlad3.Core.Models;
using Vlad3.WebControl.Models;

namespace Vlad3.WebControl.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = UserRoles.Admin)]
public sealed class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _userService.GetAllAsync(cancellationToken);
        return users.Select(user => new UserDto(user.Id, user.Username, user.Role)).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _userService.CreateUserAsync(request.Username, request.Password, request.Role, cancellationToken);
        return new UserDto(user.Id, user.Username, user.Role);
    }
}
