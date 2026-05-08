namespace Firepit.Core.Secrets;

public sealed class CompositeSecretResolver : ISecretResolver
{
    private readonly IReadOnlyList<ISecretResolver> _providers;

    public CompositeSecretResolver(params ISecretResolver[] providers)
        : this((IEnumerable<ISecretResolver>)providers)
    {
    }

    public CompositeSecretResolver(IEnumerable<ISecretResolver> providers)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
    }

    public bool TryResolve(string token, out string? value)
    {
        foreach (var provider in _providers)
        {
            if (provider.TryResolve(token, out value))
            {
                return true;
            }
        }
        value = null;
        return false;
    }
}
