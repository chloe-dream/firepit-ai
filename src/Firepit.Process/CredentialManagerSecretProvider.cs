using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Firepit.Core.Secrets;

namespace Firepit.Process;

[SupportedOSPlatform("windows10.0.17763.0")]
public sealed partial class CredentialManagerSecretProvider : ISecretResolver
{
    private const uint CRED_TYPE_GENERIC = 1;

    private static readonly Regex CredTokenRegex = new(@"^\$\{cred:([^}]+)\}$", RegexOptions.Compiled);

    public bool TryResolve(string token, out string? value)
    {
        var match = CredTokenRegex.Match(token);
        if (!match.Success)
        {
            value = null;
            return false;
        }

        var target = match.Groups[1].Value;
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credentialPtr))
        {
            value = null;
            return false;
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                value = string.Empty;
                return true;
            }

            // Credential Manager stores generic blobs as raw bytes — Windows convention is UTF-16.
            value = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return true;
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string targetName, uint type, uint reservedFlag, out IntPtr credential);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
