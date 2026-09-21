using System.Security.Cryptography;
using System.Text;

namespace SugarERP.Infrastructure.Local;

public static class DeviceCredentialProtector
{
    private const string ProtectedPrefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SugarERP.BranchType1.DeviceCredential.v1");

    public static string Protect(string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        if (credential.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return credential;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Device credentials can only be persisted by the Windows desktop client.");

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(credential),
            Entropy,
            DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string storedCredential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedCredential);
        if (!storedCredential.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The stored device credential is not protected. Re-enrollment is required.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Device credentials can only be read by the enrolled Windows user.");

        try
        {
            var protectedBytes = Convert.FromBase64String(storedCredential[ProtectedPrefix.Length..]);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "The device credential cannot be opened by this Windows user. Re-enrollment is required.",
                exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("The stored device credential is damaged. Re-enrollment is required.", exception);
        }
    }
}
