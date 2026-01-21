using TSLib.Full;

namespace Vlad3.WebControl.Utils;

public class GenerateIdentity
{
    public static void Main()
    {
        var identity = TsCrypt.GenerateNewIdentity(8); // можно 10-20, будет дольше
        Console.WriteLine("Добавь следующие настройки при конфигурации бота:");
        Console.WriteLine($"identityPrivateKey={identity.PrivateKeyString}");
        Console.WriteLine($"identityOffset={identity.ValidKeyOffset}");
    }
}