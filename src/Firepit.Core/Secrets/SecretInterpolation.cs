using System.Text.RegularExpressions;

namespace Firepit.Core.Secrets;

public static class SecretInterpolation
{
    private static readonly Regex TokenRegex = new(@"\$\{(\w+):([^}]+)\}", RegexOptions.Compiled);

    public static SecretResolveResult Interpolate(string? input, ISecretResolver resolver)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new SecretResolveResult(input ?? string.Empty, []);
        }

        var missing = new List<string>();
        var resolved = TokenRegex.Replace(input, match =>
        {
            var token = match.Value;
            if (resolver.TryResolve(token, out var value) && value is not null)
            {
                return value;
            }
            missing.Add(token);
            return token;
        });
        return new SecretResolveResult(resolved, missing);
    }
}
