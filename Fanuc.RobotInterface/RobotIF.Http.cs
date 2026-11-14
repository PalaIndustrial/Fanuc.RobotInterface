using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Fanuc.RobotInterface
{
    public partial class RobotIF
    {
        private readonly Lazy<HttpClient> HttpClient = new Lazy<HttpClient>(() => new HttpClient());
        private const string KclSuccessRegexPattern = @"<BODY.*>.*<XMP.*>\s*(?<XMP>.*)\s*</\s*XMP>.*</\s*BODY>";
        private const string KclFailRegexPattern = @"<BODY.*>.*ERROR:\s*([^<]*)\s*.*</\s*BODY>";

#if NET7_0_OR_GREATER
        [GeneratedRegex(KclSuccessRegexPattern)]
        private static partial Regex GetKclSuccessRegex();
        [GeneratedRegex(KclFailRegexPattern)]
        private static partial Regex GetKclFailRegex();
#else
        private static readonly Lazy<Regex> CachedKclSuccessRegex = new Lazy<Regex>(() => new Regex(KclSuccessRegexPattern, RegexOptions.Compiled));
        private static readonly Lazy<Regex> CachedKclFailRegex = new Lazy<Regex>(() => new Regex(KclFailRegexPattern, RegexOptions.Compiled));
        private static Regex GetKclSuccessRegex() => CachedKclSuccessRegex.Value;
        private static Regex GetKclFailRegex() => CachedKclFailRegex.Value;
#endif

        private Uri HttpUri(string path) => new Uri(new UriBuilder(Uri.UriSchemeHttp, Host, HttpPort).Uri, path);

        /// <summary>
        /// Executes a KCL command over HTTP independently of the SRTP connection.
        /// </summary>
        public string ExecuteKCL(string command)
        {
            using var response = HttpClient.Value.GetAsync(HttpUri($"KCL/{command}")).Result;
            response.EnsureSuccessStatusCode();
            var content = response.Content.ReadAsStringAsync().Result;
            var success = GetKclSuccessRegex().Match(content);
            if (success.Success)
                return success.Groups["XMP"].Value;
            var failure = GetKclFailRegex().Match(content);
            if (failure.Success)
                throw new IOException($"Executing KCL \"{command}\" failed: {failure.Groups[1].Value}");
            throw new IOException($"Executing KCL \"{command}\" failed: {content}");
        }

        /// <summary>
        /// Invokes a Karel program over HTTP independently of the SRTP connection.
        /// </summary>
        public string ExecuteKarel(string program)
        {
            using var response = HttpClient.Value.GetAsync(HttpUri($"KAREL/{program}")).Result;
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().Result;
        }
    }
}
