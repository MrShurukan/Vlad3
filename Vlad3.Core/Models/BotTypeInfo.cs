namespace Vlad3.Core.Models;

/// <summary>
/// Описание доступного типа бота.
/// </summary>
/// <param name="Type">Технический идентификатор типа.</param>
/// <param name="DisplayName">Отображаемое имя.</param>
public sealed record BotTypeInfo(string Type, string DisplayName);
