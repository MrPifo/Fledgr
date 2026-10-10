namespace YTSPlayer.Contracts;

/// <summary>
/// Kleiner Dienst-Container ohne Reflection (AOT- und Android-tauglich).
/// Der Composition Root trägt die Dienste ein, Plugins lesen sie über <see cref="IServiceProvider"/>.
/// </summary>
public sealed class ServiceRegistry : IServiceProvider
{
    readonly Dictionary<Type, object> _services = [];

    /// <summary>Trägt einen Dienst unter seinem Typ ein. Ein vorhandener Eintrag wird ersetzt.</summary>
    public ServiceRegistry Add<T>(T instance) where T : class
    {
        _services[typeof(T)] = instance;
        return this;
    }

    public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);

    /// <summary>Wie <see cref="GetService"/>, wirft aber eine Ausnahme, wenn der Dienst fehlt.</summary>
    public T Require<T>() where T : class =>
        (T?)GetService(typeof(T)) ?? throw new InvalidOperationException($"Dienst fehlt: {typeof(T).Name}");
}

/// <summary>Bequeme Zugriffe auf einen <see cref="IServiceProvider"/>.</summary>
public static class ServiceProviderExtensions
{
    public static T Require<T>(this IServiceProvider sp) where T : class =>
        (T?)sp.GetService(typeof(T)) ?? throw new InvalidOperationException($"Dienst fehlt: {typeof(T).Name}");

    public static T? Get<T>(this IServiceProvider sp) where T : class => (T?)sp.GetService(typeof(T));
}
