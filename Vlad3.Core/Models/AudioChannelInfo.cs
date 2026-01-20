namespace Vlad3.Core.Models;

/// <summary>
/// Информация о доступном аудио-канале платформы.
/// </summary>
/// <param name="Id">Идентификатор канала.</param>
/// <param name="Name">Отображаемое имя канала.</param>
public sealed record AudioChannelInfo(string Id, string Name);
