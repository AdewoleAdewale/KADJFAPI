using Java.Security;
using Java.Security.Cert;
using Javax.Net.Ssl;
using System;
using System.Net.Http;
using Xamarin.Android.Net;

namespace KADJFAPI.Services
{
    /// <summary>
    /// Builds <see cref="HttpClient"/> instances that talk through the NATIVE
    /// Android TLS stack via <see cref="AndroidClientHandler"/>.
    ///
    /// Why this exists:
    ///   The managed <c>HttpClientHandler</c> on Android cannot reliably complete
    ///   a TLS handshake with some hosts — the handshake fails BEFORE
    ///   <c>ServerCertificateCustomValidationCallback</c> ever runs, and
    ///   <c>ServicePointManager.SecurityProtocol</c> is ignored on Android.
    ///   That failure shows up as an "SSL" or "permission"/Java security error.
    ///
    ///   AndroidClientHandler uses the OS (Conscrypt) TLS stack, which negotiates
    ///   modern TLS + SNI correctly. We subclass it to trust all certificates,
    ///   preserving the bypass behaviour the old code intended.
    ///
    /// Use:  using (var client = SecureHttpClientFactory.Create()) { ... }
    /// </summary>
    public static class SecureHttpClientFactory
    {
        public static HttpClient Create(TimeSpan? timeout = null)
        {
            var handler = new TrustAllAndroidClientHandler();

            var client = new HttpClient(handler)
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.Add("User-Agent", "KadunaJudiciary-Mobile/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }
    }

    /// <summary>
    /// AndroidClientHandler that accepts any server certificate and hostname.
    /// (Matches the previous ServerCertificateCustomValidationCallback => true.)
    /// </summary>
    public sealed class TrustAllAndroidClientHandler : AndroidClientHandler
    {
        protected override SSLSocketFactory ConfigureCustomSSLSocketFactory(HttpsURLConnection connection)
        {
            var context = SSLContext.GetInstance("TLS");
            context.Init(null, new ITrustManager[] { new TrustAllManager() }, new SecureRandom());
            return context.SocketFactory;
        }

        protected override IHostnameVerifier GetSSLHostnameVerifier(HttpsURLConnection connection)
            => new TrustAllHostnameVerifier();
    }

    internal sealed class TrustAllManager : Java.Lang.Object, IX509TrustManager
    {
        public void CheckClientTrusted(X509Certificate[] chain, string authType) { }
        public void CheckServerTrusted(X509Certificate[] chain, string authType) { }
        public X509Certificate[] GetAcceptedIssuers() => new X509Certificate[0];
    }

    internal sealed class TrustAllHostnameVerifier : Java.Lang.Object, IHostnameVerifier
    {
        public bool Verify(string hostname, ISSLSession session) => true;
    }
}