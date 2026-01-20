namespace Vlad3.Application.Users;

public interface IUserStore
{
    Task<IReadOnlyList<UserRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<UserRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task AddAsync(UserRecord user, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default);
}
