using Serilog;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MWL4.Infrastructure
{
    public enum UpdateCheckOutcome
    {
        /// <summary>This version is the newest published one, or newer.</summary>
        UpToDate,
        NewVersion,
        /// <summary>GitHub answers, but nothing has been published.</summary>
        NoRelease,
        /// <summary>No answer or one that cannot be read. Normal on a closed hospital network.</summary>
        Failed,
    }

    public sealed class UpdateCheckResult
    {
        public UpdateCheckOutcome Outcome { get; set; }
        /// <summary>The tag as published, without a leading v. Only set for a new version.</summary>
        public string LatestVersion { get; set; }
        public Uri ReleaseUri { get; set; }
        /// <summary>The release's description as written on GitHub, in Markdown. Only set for a new version, and may be empty.</summary>
        public string ReleaseNotes { get; set; }
    }

    /// <summary>Asks GitHub for the newest release and compares it with this version.</summary>
    public sealed class UpdateChecker
    {
        private const string TrustedPrefix = "https://github.com/";

        [DataContract]
        private sealed class ReleaseInfo
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }

            [DataMember(Name = "html_url")]
            public string HtmlUrl { get; set; }

            [DataMember(Name = "body")]
            public string Body { get; set; }
        }

        private readonly string _currentVersion;
        private readonly string _url;
        private readonly TimeSpan _timeout;
        private readonly Func<HttpMessageHandler> _createHandler;

        /// <param name="handler">Replaces the network in tests.</param>
        public UpdateChecker(string currentVersion, string url = AppInfo.LatestReleaseApiUrl, HttpMessageHandler handler = null, TimeSpan? timeout = null)
        {
            _currentVersion = currentVersion;
            _url = url;
            _timeout = timeout ?? TimeSpan.FromSeconds(5);
            _createHandler = () => handler ?? new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials };
        }

        /// <summary>Never throws for a failed check, only if <paramref name="token"/> is cancelled.</summary>
        public async Task<UpdateCheckResult> CheckAsync(CancellationToken token = default(CancellationToken))
        {
            try
            {
                using (var client = new HttpClient(_createHandler()) { Timeout = _timeout })
                using (var request = new HttpRequestMessage(HttpMethod.Get, _url))
                {
                    // GitHub refuses a request without a User-Agent.
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DMWLT4", ToHeaderToken(_currentVersion)));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                    using (var response = await client.SendAsync(request, token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheckResult { Outcome = UpdateCheckOutcome.NoRelease };
                        if (!response.IsSuccessStatusCode)
                        {
                            Log.ForContext<UpdateChecker>().Warning("Version check: GitHub answered {status}", (int)response.StatusCode);
                            return Failed();
                        }

                        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        return Interpret(body, _currentVersion);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is InvalidOperationException)
            {
                // OperationCanceledException without the token being cancelled is HttpClient's timeout.
                Log.ForContext<UpdateChecker>().Warning("Version check failed: {message}", ex.GetBaseException().Message);
                return Failed();
            }
        }

        private static UpdateCheckResult Failed() => new UpdateCheckResult { Outcome = UpdateCheckOutcome.Failed };

        private static string ToHeaderToken(string version)
        {
            var parsed = TryParseVersion(version, out var value);
            return parsed ? value.ToString(3) : "0";
        }

        private static UpdateCheckResult Interpret(byte[] body, string currentVersion)
        {
            ReleaseInfo release;
            try
            {
                using (var stream = new MemoryStream(body))
                {
                    release = (ReleaseInfo)new DataContractJsonSerializer(typeof(ReleaseInfo)).ReadObject(stream);
                }
            }
            catch (SerializationException ex)
            {
                Log.ForContext<UpdateChecker>().Warning("Version check: could not read GitHub's answer: {message}", ex.Message);
                return Failed();
            }

            if (!TryParseVersion(release?.TagName, out var latest) || !TryParseVersion(currentVersion, out var current))
            {
                Log.ForContext<UpdateChecker>().Warning("Version check: cannot compare '{latest}' with '{current}'", release?.TagName, currentVersion);
                return Failed();
            }

            if (latest <= current) return new UpdateCheckResult { Outcome = UpdateCheckOutcome.UpToDate };
            return new UpdateCheckResult
            {
                Outcome = UpdateCheckOutcome.NewVersion,
                LatestVersion = release.TagName.Trim().TrimStart('v', 'V'),
                ReleaseUri = SafeReleaseUri(release.HtmlUrl),
                ReleaseNotes = release.Body,
            };
        }

        /// <summary>The link is opened in a browser when clicked, so only a page on GitHub is taken from the answer.</summary>
        private static Uri SafeReleaseUri(string url)
        {
            if (url != null && url.StartsWith(TrustedPrefix, StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri;
            }
            return new Uri(AppInfo.ProjectUrl + "/releases/latest");
        }

        /// <summary>
        /// Reads 1.4, 1.1.0, v1.2.0 and 1.2.0-rc1, with missing parts as 0. System.Version alone would call 1.1 older than
        /// 1.1.0, because a missing part counts as -1, and would refuse a bare 2.
        /// </summary>
        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var cleaned = text.Trim().TrimStart('v', 'V');
            var suffix = cleaned.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) cleaned = cleaned.Substring(0, suffix);
            if (cleaned.IndexOf('.') < 0) cleaned += ".0";

            if (!Version.TryParse(cleaned, out var parsed)) return false;
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
            return true;
        }
    }
}
