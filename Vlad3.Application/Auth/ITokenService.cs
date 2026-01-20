using Vlad3.Application.Users;

namespace Vlad3.Application.Auth;

public interface ITokenService
{
    TokenResult CreateToken(UserRecord user);
}
