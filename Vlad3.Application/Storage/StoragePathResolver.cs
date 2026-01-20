using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Vlad3.Application.Options;

namespace Vlad3.Application.Storage;

public sealed class StoragePathResolver
{
    private readonly StorageOptions _options;
    private readonly string _contentRoot;

    public StoragePathResolver(IOptions<StorageOptions> options, IHostEnvironment hostEnvironment)
    {
        _options = options.Value;
        _contentRoot = hostEnvironment.ContentRootPath;
    }

    public string RootPath
        => Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(_contentRoot, _options.RootPath);

    public string UsersFile => Path.Combine(RootPath, "users.json");
    public string BotsFile => Path.Combine(RootPath, "bots.json");
    public string PlaylistsRoot => Path.Combine(RootPath, "playlists");

    public string GetPlaylistFolder(string playlistId) => Path.Combine(PlaylistsRoot, playlistId);
    public string GetPlaylistFile(string playlistId) => Path.Combine(GetPlaylistFolder(playlistId), "playlist.json");
    public string GetPlaylistFilesFolder(string playlistId) => Path.Combine(GetPlaylistFolder(playlistId), "files");
}
