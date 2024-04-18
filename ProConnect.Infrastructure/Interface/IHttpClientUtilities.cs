using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IHttpClientUtilities
    {
        Task<T> GetAsync<T>(string baseUrl, string path, string authorizationToken = null, Dictionary<string, string> headers = null);
        Task<T> GetAsync<T>(string baseUrl, string path, TimeSpan timeSpan, string authorizationToken = null, Dictionary<string, string> headers = null);
        Task DeleteAsync<T>(string baseUrl, string path, string authorizationToken = null);
        Task<T> PostAsync<T>(string baseUrl, string path, object request, string authorizationToken = null, Dictionary<string, string> headers = null);
        Task<T> PostAsync<T>(string fullpath, object request, string authorizationToken = null, Dictionary<string, string> headers = null);
        Task<T> PostAsyncGraphQL<T>(string baseUrl, string path, string request, string authorizationToken = null);
        Task<T> PostAsyncFormUrlEncoded<T>(string baseUrl, string path, Dictionary<string, string> request, string authorizationToken = null, Dictionary<string, string> headers = null);
        Task<T> PostAsync<T>(string baseUrl, string path, object request, TimeSpan timeout, string authorizationToken = null, Dictionary<string, string> headers = null);
        Task<T> PutAsync<T>(string baseUrl, string path, object request, string authorizationToken = null);

        Task<string> PostAsyncNonJson(
            string baseUrl,
            string path,
            object request,
            string authorizationToken = null,
            Dictionary<string, string> headers = null
        );

        void AddAuthorizationHeader(HttpClient httpClient, string headerValue);
        void AddHttpHeader(HttpClient httpClient, string headerKey, string headerValue);

        string GetFullPath(string baseUrl, string path, bool withForwardSlash = true);
        Task<T> DeserializeJsonResponse<T>(HttpResponseMessage response);
    }
}
