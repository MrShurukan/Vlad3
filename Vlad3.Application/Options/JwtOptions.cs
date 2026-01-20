namespace Vlad3.Application.Options;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "Vlad3";
    public string Audience { get; set; } = "Vlad3";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 120;
}
