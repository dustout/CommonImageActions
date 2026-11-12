using System;
using System.Security.Cryptography;
using System.Text;

namespace CommonImageActions.AspNetCore
{
    /// <summary>
    /// Signs image urls so that the middleware can tell a url your site made from one somebody
    /// typed in. Use it wherever an image url with a query string is written, with the same key
    /// that is set in <see cref="CommonImageActionSettings.SigningKey"/>.
    /// </summary>
    public static class CommonImageActionsUrlSigner
    {
        /// <summary>
        /// The query string parameter that holds the signature. It is always the last parameter.
        /// </summary>
        public const string SignatureParameter = "sig";

        /// <summary>
        /// Add a signature to a url. The url is the path and query string as it is written in the
        /// page, for example <c>/photos/cat.jpg?w=200</c> becomes <c>/photos/cat.jpg?w=200&amp;sig=...</c>
        /// </summary>
        public static string SignUrl(string url, string signingKey)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            var signature = CreateSignature(url, signingKey);
            var separator = url.Contains("?") ? "&" : "?";
            return $"{url}{separator}{SignatureParameter}={signature}";
        }

        /// <summary>
        /// The signature for a url (path and query string) that does not have one yet.
        /// </summary>
        public static string CreateSignature(string url, string signingKey)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            var queryIndex = url.IndexOf('?');
            var path = queryIndex >= 0 ? url.Substring(0, queryIndex) : url;
            var query = queryIndex >= 0 ? url.Substring(queryIndex) : string.Empty;

            //the server sees the path with any %20 style escaping already removed, so sign it that way
            return CreateSignature(Uri.UnescapeDataString(path), query, signingKey);
        }

        /// <summary>
        /// Check the signature on a request.
        /// </summary>
        /// <param name="path">The path of the request, not escaped.</param>
        /// <param name="queryString">The whole query string, starting with the question mark.</param>
        public static bool IsValid(string path, string queryString, string signingKey)
        {
            if (string.IsNullOrEmpty(queryString) || string.IsNullOrEmpty(signingKey))
            {
                return false;
            }

            //the signature has to be the last parameter, everything before it is what was signed
            var marker = $"{SignatureParameter}=";
            var index = queryString.LastIndexOf(marker, StringComparison.Ordinal);
            if (index < 1 || (queryString[index - 1] != '?' && queryString[index - 1] != '&'))
            {
                return false;
            }

            var signature = queryString.Substring(index + marker.Length);
            if (signature.Length == 0 || signature.Contains("&"))
            {
                return false;
            }

            var signedQuery = queryString.Substring(0, index - 1);
            var expected = CreateSignature(path ?? string.Empty, signedQuery, signingKey);

            //compare in a way that takes the same time whether or not the start of the signature is right
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
        }

        private static string CreateSignature(string path, string query, string signingKey)
        {
            if (string.IsNullOrEmpty(signingKey))
            {
                throw new ArgumentNullException(nameof(signingKey));
            }

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{path}{query}"));
                var result = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    result.Append(b.ToString("x2"));
                }

                return result.ToString();
            }
        }
    }
}
