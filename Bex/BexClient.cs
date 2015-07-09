﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bex.Exceptions;
using Bex.Extensions;
using Newtonsoft.Json;

namespace Bex
{
    public class BexClient
    {
        private const string BaseHealthUri = "https://api.microsofthealth.net/v1/me/";
        private const string RedirectUri = "https://login.live.com/oauth20_desktop.srf";
        private const string AuthUrl = "https://login.live.com/oauth20_authorize.srf";
        private const string SignOutUrl = "https://login.live.com/oauth20_logout.srf";
        private const string TokenUrl = "https://login.live.com/oauth20_token.srf";

        private readonly HttpClient _httpClient;

        /// <summary>
        /// Gets the client secret. This can be got from https://account.live.com/developers/applications
        /// </summary>
        public string ClientSecret { get; }

        /// <summary>
        /// Gets the client identifier. This can be got from https://account.live.com/developers/applications
        /// </summary>
        public string ClientId { get; }

        /// <summary>
        /// Gets the credentials.
        /// </summary>
        public LiveIdCredentials Credentials { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BexClient"/> class.
        /// </summary>
        /// <param name="clientSecret">The client secret.</param>
        /// <param name="clientId">The client identifier.</param>
        public BexClient(string clientSecret, string clientId)
            : this(clientSecret, clientId, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BexClient"/> class.
        /// </summary>
        /// <param name="clientSecret">The client secret.</param>
        /// <param name="clientId">The client identifier.</param>
        /// <param name="handler">The handler.</param>
        public BexClient(string clientSecret, string clientId, HttpMessageHandler handler)
        {
            ClientSecret = clientSecret;
            ClientId = clientId;

            var messageHandler = handler ??
                                 new HttpClientHandler
                                 {
                                     AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
                                 };

            _httpClient = new HttpClient(messageHandler);
        }

        /// <summary>
        /// Sets the credentials.
        /// </summary>
        /// <param name="credentials">The credentials.</param>
        public void SetCredentials(LiveIdCredentials credentials)
        {
            Credentials = credentials;
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"bearer {Credentials.AccessToken}");
        }

        /// <summary>
        /// Creates the authentication URL.
        /// </summary>
        /// <param name="scopes">The scopes.</param>
        /// <returns></returns>
        public string CreateAuthenticationUrl(List<Scope> scopes)
        {
            var uriBuilder = new UriBuilder(AuthUrl);
            var query = new StringBuilder();

            query.AppendFormat("redirect_uri={0}", Uri.EscapeUriString(RedirectUri));
            query.AppendFormat("&client_id={0}", Uri.EscapeUriString(ClientId));

            var scopesString = string.Join(" ", scopes.Select(x => x.GetDescription()));
            query.AppendFormat("&scope={0}", Uri.EscapeUriString(scopesString));
            query.Append("&response_type=code");

            uriBuilder.Query = query.ToString();

            return uriBuilder.Uri.ToString();
        }

        /// <summary>
        /// Creates the sign out URL.
        /// </summary>
        /// <returns></returns>
        public string CreateSignOutUrl()
        {
            UriBuilder uriBuilder = new UriBuilder(SignOutUrl);
            var query = new StringBuilder();

            query.AppendFormat("redirect_uri={0}", Uri.EscapeUriString(RedirectUri));
            query.AppendFormat("&client_id={0}", Uri.EscapeUriString(ClientId));

            uriBuilder.Query = query.ToString();

            return uriBuilder.Uri.ToString();
        }

        /// <summary>
        /// Exchanges the code asynchronous.
        /// </summary>
        /// <param name="code">The code. If performing a refresh of the token, this should be the RefreshToken</param>
        /// <param name="isTokenRefresh">if set to <c>true</c> [is token refresh].</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">code cannot be null or empty</exception>
        public async Task<LiveIdCredentials> ExchangeCodeAsync(string code, bool isTokenRefresh = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentNullException(nameof(code), "code cannot be null or empty");
            }

            var postData = new Dictionary<string, string>
            {
                {"redirect_uri", Uri.EscapeUriString(RedirectUri)},
                {"client_id", Uri.EscapeUriString(ClientId)},
                {"client_secret", Uri.EscapeUriString(ClientSecret)}
            };

            if (isTokenRefresh)
            {
                postData.Add("refresh_token", Uri.EscapeUriString(code));
                postData.Add("grant_type", "refresh_token");
            }
            else
            {
                postData.Add("code", Uri.EscapeUriString(code));
                postData.Add("grant_type", "authorization_code");
            }

            var response = await GetResponse<LiveIdCredentials>("", postData, cancellationToken, TokenUrl);
            SetCredentials(response);

            return response;
        }

        /// <summary>
        /// Gets the profile asynchronous.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The user's profile</returns>
        public async Task<object> GetProfileAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await ValidateCredentials();

            var response = await GetResponse<object>("Profile", new Dictionary<string, string>(), cancellationToken);

            return response;
        }

        private async Task<TReturnType> GetResponse<TReturnType>(string path, Dictionary<string, string> postData,
            CancellationToken cancellationToken = default(CancellationToken), string altBaseUrl = null)
        {
            var uri = new UriBuilder(altBaseUrl ?? BaseHealthUri);
            uri.Path += path;

            var queryParams = string.Join("&", postData.Select(x => $"{x.Key}={x.Value}"));
            uri.Query = queryParams;

            var response = await _httpClient.GetAsync(uri.Uri, cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            var item = JsonConvert.DeserializeObject<TReturnType>(responseString);
            return item;
        }

        private Task<bool> ValidateCredentials()
        {
            if (string.IsNullOrEmpty(Credentials?.AccessToken))
            {
                throw new BexException("NoCreds", "No valide credentials have been set");
            }

            return Task.FromResult(true);
        }
    }
}