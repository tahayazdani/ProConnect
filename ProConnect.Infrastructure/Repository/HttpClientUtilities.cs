using Microsoft.Extensions.Logging;
using ProConnect.Infrastructure.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class HttpClientUtilities : IHttpClientUtilities
    {
        private readonly ILogger<HttpClientUtilities> _logger;
        private readonly IHttpClientFactory _factory;

        public HttpClientUtilities(
            ILogger<HttpClientUtilities> logger,
            IHttpClientFactory httpClientFactory
        )
        {
            _logger = logger;
            _factory = httpClientFactory;
        }

        private readonly JsonSerializerOptions _defaultOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public async Task<T> DeserializeJsonResponse<T>(
            HttpResponseMessage response
        )
        {
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(responseBody))
            {
                return default;
            }

            T result = JsonSerializer.Deserialize<T>(responseBody, _defaultOptions);
            return result;
        }

        public void AddAuthorizationHeader(
            HttpClient httpClient,
            string headerValue
        )
        {
            const string AUTHORIZATION_HEADER = "Authorization";

            if (headerValue.ToLowerInvariant().Contains("bearer"))
            {
                AddHttpHeader(httpClient, AUTHORIZATION_HEADER, headerValue);
            }
            else
            {
                AddHttpHeader(httpClient, AUTHORIZATION_HEADER, "Bearer " + headerValue);
            }
        }

        public void AddHttpHeader(
            HttpClient httpClient,
            string headerKey,
            string headerValue
        )
        {
            var result = httpClient.DefaultRequestHeaders.TryAddWithoutValidation(headerKey, headerValue);
            if (!result)
            {
                _logger.LogWarning($"Failed to add {headerKey} header with value {headerValue} to {httpClient.BaseAddress?.AbsoluteUri}");
            }
        }

        public async Task<T> GetAsync<T>(
            string baseUrl,
            string path,
            TimeSpan timeout,
            string authorizationToken = null,
            Dictionary<string, string> headers = null
            )
        {
            string url = GetFullPath(baseUrl, path);
            var httpClient = _factory.CreateClient();
            httpClient.Timeout = timeout;

            if (!string.IsNullOrEmpty(authorizationToken))
            {
                this.AddAuthorizationHeader(httpClient, authorizationToken);
            }

            if (headers != null)
            {
                foreach (var pair in headers)
                {
                    this.AddHttpHeader(httpClient, pair.Key, pair.Value);
                }
            }

            _logger.LogDebug($"Executing get request to: '{url}'");
            var response = await httpClient.GetAsync(url);
            var result = await this.DeserializeJsonResponse<T>(response);
            return result;
        }

        public async Task<T> GetAsync<T>(
            string baseUrl,
            string path,
            string authorizationToken = null,
            Dictionary<string, string> headers = null
        )
        {
            TimeSpan defaultTimeoutValue = TimeSpan.FromSeconds(100);

            return await GetAsync<T>(baseUrl,
                path,
                defaultTimeoutValue,
                authorizationToken,
                headers);
        }

        public async Task<T> PostAsync<T>(
            string baseUrl,
            string path,
            object request,
            string authorizationToken = null,
            Dictionary<string, string> headers = null
        )
        {
            TimeSpan defaultTimeoutValue = TimeSpan.FromSeconds(100);

            return await PostAsync<T>(baseUrl,
                path,
                request,
                defaultTimeoutValue,
                authorizationToken,
                headers
                );
        }

        public async Task<T> PostAsync<T>(string url, object request, string authorizationToken = null, Dictionary<string, string> headers = null)
        {
            TimeSpan defaultTimeoutValue = TimeSpan.FromSeconds(100);

            return await PostAsync<T>(request, defaultTimeoutValue, authorizationToken, headers, url);
        }

        public async Task<T> PostAsync<T>(
            string baseUrl,
            string path,
            object request,
            TimeSpan timeout,
            string authorizationToken = null,
            Dictionary<string, string> headers = null
        )
        {
            string url = GetFullPath(baseUrl, path);

            return await PostAsync<T>(request, timeout, authorizationToken, headers, url);
        }

        private async Task<T> PostAsync<T>(object request, TimeSpan timeout, string authorizationToken, Dictionary<string, string> headers, string url)
        {
            try
            {
                var httpClient = _factory.CreateClient();
                httpClient.Timeout = timeout;

                if (!string.IsNullOrEmpty(authorizationToken))
                {
                    this.AddAuthorizationHeader(httpClient, authorizationToken);
                }

                if (headers != null)
                {
                    foreach (var pair in headers)
                    {
                        this.AddHttpHeader(httpClient, pair.Key, pair.Value);
                    }
                }

                var json = JsonSerializer.Serialize(request);
                var data = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug($"Executing post request to: '{url}'");
                var response = await httpClient.PostAsync(url, data);
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Error happened from {url} endpoint");

                var result = await this.DeserializeJsonResponse<T>(response);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to post data to: '{url}' , exception: '{ex}'");
                throw;
            }
        }

        public async Task<string> PostAsyncNonJson(
            string baseUrl,
            string path,
            object request,
            string authorizationToken = null,
            Dictionary<string, string> headers = null
        )
        {
            string url = GetFullPath(baseUrl, path);

            try
            {
                TimeSpan defaultTimeoutValue = TimeSpan.FromSeconds(100);
                var httpClient = _factory.CreateClient();
                httpClient.Timeout = defaultTimeoutValue;

                if (!string.IsNullOrEmpty(authorizationToken))
                {
                    this.AddAuthorizationHeader(httpClient, authorizationToken);
                }

                if (headers != null)
                {
                    foreach (var pair in headers)
                    {
                        this.AddHttpHeader(httpClient, pair.Key, pair.Value);
                    }
                }

                var json = JsonSerializer.Serialize(request);
                var data = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug($"Executing post request to: '{url}'");
                var response = await httpClient.PostAsync(url, data);

                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(responseBody))
                {
                    return default;
                }

                return responseBody;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to post data to: '{url}' , exception: '{ex}'");
                throw;
            }
        }

        public async Task<T> PutAsync<T>(
            string baseUrl,
            string path,
            object request,
            string authorizationToken = null
        )
        {
            string url = GetFullPath(baseUrl, path);

            try
            {
                var httpClient = _factory.CreateClient();

                if (!string.IsNullOrEmpty(authorizationToken))
                {
                    this.AddAuthorizationHeader(httpClient, authorizationToken);
                }

                var json = JsonSerializer.Serialize(request);
                var data = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug($"Executing put request to: '{url}'");
                var response = await httpClient.PutAsync(url, data);
                var result = await this.DeserializeJsonResponse<T>(response);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to put data to: '{url}' , exception: '{ex}'");
                throw;
            }
        }

        public async Task DeleteAsync<T>(
            string baseUrl,
            string path,
            string authorizationToken = null
            )
        {
            string url = GetFullPath(baseUrl, path);

            try
            {
                var httpClient = _factory.CreateClient();

                if (!string.IsNullOrEmpty(authorizationToken))
                {
                    this.AddAuthorizationHeader(httpClient, authorizationToken);
                }

                _logger.LogDebug($"Executing delete request to: '{url}'");
                object o = await httpClient.DeleteAsync(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to delete data from: '{url}'");
                throw;
            }
        }

        public async Task<T> PostAsyncGraphQL<T>(
            string baseUrl,
            string path,
            string request,
            string authorizationToken = null
        )
        {
            string url = GetFullPath(baseUrl, path);

            try
            {
                var httpClient = _factory.CreateClient();

                if (!string.IsNullOrEmpty(authorizationToken))
                {
                    this.AddAuthorizationHeader(httpClient, authorizationToken);
                }

                var data = new StringContent(request, Encoding.UTF8, "application/json");

                _logger.LogDebug($"Executing post graph ql request to: '{url}'");
                var response = await httpClient.PostAsync(url, data);
                var result = await this.DeserializeJsonResponse<T>(response);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to post data to: '{url}' , exception: '{ex}'");
                throw;
            }
        }
        public string GetFullPath(string baseUrl, string path, bool withForwardSlash = true)
        {
            const string forwardSlash = "/";

            var url = baseUrl;

            if (url.EndsWith(forwardSlash))
                url = url[0..^1];

            if (withForwardSlash && !path.StartsWith(forwardSlash))
                url += forwardSlash;

            url += path;
            return url;
        }

        public async Task<T> PostAsyncFormUrlEncoded<T>(string baseUrl, string path, Dictionary<string, string> request, string authorizationToken = null, Dictionary<string, string> headers = null)
        {
            string url = GetFullPath(baseUrl, path);
            try
            {
                var httpClient = _factory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(100);

                if (!string.IsNullOrEmpty(authorizationToken))
                {
                    this.AddAuthorizationHeader(httpClient, authorizationToken);
                }

                if (headers != null)
                {
                    foreach (var pair in headers)
                    {
                        this.AddHttpHeader(httpClient, pair.Key, pair.Value);
                    }
                }

                var content = new FormUrlEncodedContent(request);
                _logger.LogDebug($"Executing post request to: '{url}'");

                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
                var response = await httpClient.PostAsync(url, content);

                _logger.LogDebug($"Response: '{response.Content.ReadAsStringAsync()}'");

                if (response.IsSuccessStatusCode)
                {
                    return await DeserializeJsonResponse<T>(response);
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to post data to: '{url}' , exception: '{ex}'");
                throw;
            }
        }
    }
}
