using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Vlad3.Core.Abstractions;

namespace Vlad3.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAudioBotFactories(this IServiceCollection services)
    {
        var factoryType = typeof(IAudioBotFactory);
        LoadBotAssemblies();
        var factories = new HashSet<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (type is null || type.IsAbstract || !factoryType.IsAssignableFrom(type))
                {
                    continue;
                }

                factories.Add(type);
            }
        }

        foreach (var factory in factories)
        {
            services.AddSingleton(factoryType, factory);
        }

        return services;
    }

    private static IEnumerable<Type?> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types;
        }
    }

    private static void LoadBotAssemblies()
    {
        var baseDirectory = AppContext.BaseDirectory;
        foreach (var path in Directory.EnumerateFiles(baseDirectory, "Vlad3.Bots.*.dll"))
        {
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(path);
                if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == assemblyName.Name))
                {
                    continue;
                }

                Assembly.LoadFrom(path);
            }
            catch
            {
                // Ignore load errors to keep startup resilient.
            }
        }
    }
}
