namespace Firepit.Core.Secrets;

public interface ISecretResolver
{
    bool TryResolve(string token, out string? value);
}

public sealed record SecretResolveResult(string ResolvedValue, IReadOnlyList<string> MissingTokens);
