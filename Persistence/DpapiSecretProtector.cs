using System.Security.Cryptography;
using System.Text;

namespace MediaWorkflowOrchestrator.Persistence
{
    public sealed class DpapiSecretProtector : ISecretProtector
    {
        public string Protect(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
            {
                return string.Empty;
            }

            var bytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string Unprotect(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
            {
                return string.Empty;
            }

            try
            {
                var bytes = Convert.FromBase64String(cipherText);
                var unprotectedBytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(unprotectedBytes);
            }
            catch (FormatException)
            {
                return string.Empty;
            }
            catch (CryptographicException)
            {
                return string.Empty;
            }
        }
    }
}
