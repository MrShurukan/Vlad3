namespace Vlad3.Core.Models;

/// <summary>
/// Роли пользователей для авторизации.
/// </summary>
public static class UserRoles
{
    /// <summary>
    /// Полные права администратора.
    /// </summary>
    public const string Admin = "admin";

    /// <summary>
    /// Права оператора без изменения конфигурации.
    /// </summary>
    public const string Operator = "operator";
}
