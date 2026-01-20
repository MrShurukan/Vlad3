namespace Vlad3.Core.Models;

/// <summary>
/// Конфигурация экземпляра аудио-бота.
/// </summary>
/// <param name="Id">Идентификатор экземпляра.</param>
/// <param name="Type">Тип реализации (например, discord/teamspeak).</param>
/// <param name="Label">Отображаемая метка для UI.</param>
/// <param name="ApiKey">Ключ/токен доступа платформы.</param>
/// <param name="Settings">Дополнительные настройки реализации.</param>
public sealed record BotConfiguration(
    string Id,
    string Type,
    string Label,
    string ApiKey,
    IReadOnlyDictionary<string, string> Settings);
