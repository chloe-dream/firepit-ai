using System.Text.RegularExpressions;

namespace Firepit.Core.Secrets;

public sealed class EnvironmentSecretProvider : ISecretResolver
{
    private static readonly Regex EnvTokenRegex = new(@"^\$\{env:(\w+)\}$", RegexOptions.Compiled);

    public bool TryResolve(string token, out string? value)
    {
        var match = EnvTokenRegex.Match(token);
        if (!match.Success)
        {
            value = null;
            return false;
        }

        var name = match.Groups[1].Value;
        value = Environment.GetEnvironmentVariable(name);
        return value is not null;
    }
}
