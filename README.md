# Vlad3

Vlad3 — серверный менеджер для управления аудио‑ботами разных платформ. Центральная логика построена через core абстракцию: 
единый запуск, несколько подключаемых библиотек с реализациями ботов, управление через REST и команды самой платформы (например, Discord slash‑commands).

## Основные компоненты

- `Vlad3.WebControl` — ASP.NET Core API, авторизация (JWT), REST‑управление ботами и плейлистами.
- `Vlad3.Application` — прикладные сервисы: менеджер ботов, плейлисты, пользователи, конфигурации.
- `Vlad3.Core` — общие абстракции и модели (интерфейс бота, состояния, команды).
- `Vlad3.Bots.*` — реализации аудио‑ботов для конкретных платформ.

## Реализации ботов

- **Discord** — `Vlad3.Bots.Discord` на базе NetCord (Gateway + Voice + Slash‑commands).
- **TeamSpeak** — `Vlad3.Bots.TeamSpeak` на базе Splamy.TSLib (full‑client + voice).

## Конфигурация Discord‑бота

Для создания Discord‑бота через API требуется указать:

- `ApiKey` — токен бота Discord.
- `settings.guildId` — ID сервера (используется для чтения голосовых каналов).
- `settings.commandGuildId` — ID сервера для быстрой регистрации slash‑команд. Если не задано — команды регистрируются глобально (дольше).
- `settings.ffmpegPath` — путь к `ffmpeg` (если не в `PATH`).

## Конфигурация TeamSpeak‑бота

Для создания TeamSpeak‑бота через API требуется указать:

- `settings.address` — адрес вида `host:port` (альтернатива `settings.host` + `settings.port`).
- `settings.host` / `settings.port` — адрес и порт TeamSpeak (по умолчанию `9987`).
- `settings.nickname` — имя клиента (если не задано, берется label бота).
- `settings.serverPassword` — пароль сервера (если требуется).
- `settings.defaultChannel` — канал по умолчанию (путь или id, можно пустым).
- `settings.channelPassword` — пароль канала (если требуется).
- `settings.identity` — TeamSpeak identity (если есть).
- `settings.identityPrivateKey` / `settings.identityOffset` — альтернативный формат identity (если задано).
- `settings.ffmpegPath` — путь к `ffmpeg` (если не в `PATH`).
- `settings.autoNext` — включить авто‑переход (по умолчанию `true`).

## Команды TeamSpeak (чат)

Примеры команд, которые понимает бот:

- `!play <playlistId> [trackId]`
- `!stop`, `!next`, `!previous`
- `!connect <channelId>`, `!disconnect`
- `!join` — подключиться в канал автора команды
- `!autonext` — переключение auto‑next

## Зависимости для Voice (Discord)

Для корректной работы голосового режима:

- `ffmpeg` должен быть доступен в `PATH` **или** указан через `settings.ffmpegPath`.
- Библиотеки `libsodium` и `opus` нужно положить в папку с исполняемым файлом сервера.
- [Инструкция от NetCord](https://netcord.dev/guides/basic-concepts/installing-native-dependencies.html?tabs=dynamic).

## TODO:
- UI