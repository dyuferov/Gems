// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Gems.Logging.Mvc.LogsCollector;
using Gems.Metrics;
using Gems.Metrics.Http;
using Gems.Mvc;
using Gems.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Newtonsoft.Json.Linq;

namespace Gems.Http
{
    public abstract class BaseClientService<TDefaultError>
    {
        private readonly IOptions<HttpClientServiceOptions> options;
        private readonly ILogger logger;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly IMetricsService metricsService;
        private readonly Func<ILogger, RequestLogsCollector> logsCollectorFactoryMethod;

        protected BaseClientService(IOptions<HttpClientServiceOptions> options, BaseClientServiceHelper helper)
        {
            this.options = options;
            this.logger = helper.Logger;
            this.metricsService = helper.MetricsService;
            this.logsCollectorFactoryMethod = helper.LogsCollectorFactory != null
                ? helper.LogsCollectorFactory.Create
                : this.CreateRequestLogsCollector;
            this.httpClientFactory = helper.HttpClientFactory;
        }

        /// <summary>
        /// Получает последнее исключение, которое возникло при вызове метода TrySendRequestAsync.
        /// </summary>
        public Exception LastException { get; private set; }

        /// <summary>
        /// MediaType по умолчанию.
        /// </summary>
        protected virtual string MediaType => "application/json";

        /// <summary>
        /// Базовый url сервиса.
        /// </summary>
        protected virtual string BaseUrl => this.options?.Value?.BaseUrl ?? string.Empty;

        /// <summary>
        /// Начиная с какого http статуса делать повторные запросы в случае ошибки. По умолчанию 499.
        /// Переопределяет значение из настроек.
        /// </summary>
        protected virtual int DurableFromHttpStatus => this.options?.Value?.DurableFromHttpStatus ?? 499;

        /// <summary>
        /// true - запрос будет повторяться указанное количество раз в случае ошибки.
        /// Переопределяет значение из настроек.
        /// </summary>
        protected virtual bool Durable => this.options?.Value?.Durable ?? false;

        /// <summary>
        /// Количество повторных попыток в случае если отправка запроса не удалась.
        /// Переопределяет значение из настроек.
        /// </summary>
        protected virtual short Attempts => this.options?.Value?.Attempts ?? 3;

        /// <summary>
        /// Количество милисекунд задержки перед повторной отправкой.
        /// Переопределяет значение из настроек.
        /// </summary>
        protected virtual int MillisecondsDelay => this.options?.Value?.MillisecondsDelay ?? 5000;

        /// <summary>
        /// Время ожидания в милисекундах для выполнения запроса.
        /// Переопределяет значение из настроек.
        /// </summary>
        protected virtual int RequestTimeout => this.options?.Value?.RequestTimeout ?? 0;

        /// <summary>
        /// Необходимо загрузить сертификат.
        /// </summary>
        protected virtual bool NeedDownloadCertificate => this.options?.Value?.NeedDownloadCertificate ?? false;

        /// <summary>
        /// Конвертеры для десериализации в json.
        /// </summary>
        protected virtual IList<JsonConverter> DeserializeAdditionalConverters => null;

        /// <summary>
        /// Конвертеры для сериализации из json.
        /// </summary>
        protected virtual IList<JsonConverter> SerializeAdditionalConverters => null;

        /// <summary>
        /// true - сериализовать в Camel Case.
        /// </summary>
        protected virtual bool IsCamelCase => true;

        /// <summary>
        /// DefaultBoundary для multipart/form-data.
        /// </summary>
        protected virtual string DefaultBoundary => "-------------------Boundary--";

        public async Task<(TResponse, TError)> TrySendRequestAsync<TResponse, TError>(
            HttpMethod httpMethod,
            string requestUri,
            object requestData,
            IDictionary<string, string> headers,
            CancellationToken cancellationToken)
        {
            TResponse response = default;
            TError error = default;
            try
            {
                response = await this.SendRequestAsync<TResponse, TError>(httpMethod, requestUri, requestData, headers, cancellationToken);
            }
            catch (RequestException<TError> e)
            {
                this.LastException = e;
                error = e.Error;
            }

            return (response, error);
        }

        public virtual async Task<TResponse> SendRequestAsync<TResponse, TError>(
            HttpMethod httpMethod,
            string requestUri,
            object requestData,
            IDictionary<string, string> headers,
            CancellationToken cancellationToken)
        {
            Task<TResponse> SendRequestInnerAsync() => this.SendWithHandlingExceptionsAsync<TResponse, TError>(
                httpMethod,
                requestUri,
                requestData,
                headers,
                cancellationToken);

            return this.Durable
                ? await this.DurableRequestAsync<TResponse, TError>(SendRequestInnerAsync, cancellationToken).ConfigureAwait(false)
                : await SendRequestInnerAsync().ConfigureAwait(false);
        }

        public Task<TResponse> GetWithCustomErrorAsync<TResponse, TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<Unit> GetWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<string> GetStringWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<string, TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<Stream> GetStreamWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Stream, TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<byte[]> GetByteArrayWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<byte[], TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<TResponse> GetWithCustomErrorAsync<TResponse, TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<Unit> GetWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<string> GetStringWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<string, TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<Stream> GetStreamWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Stream, TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<byte[]> GetByteArrayWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<byte[], TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<TResponse> DeleteWithCustomErrorAsync<TResponse, TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Delete, requestUri, null, headers, cancellationToken);
        }

        public Task<Unit> DeleteWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Delete, requestUri, null, headers, cancellationToken);
        }

        public Task<TResponse> DeleteWithCustomErrorAsync<TResponse, TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Delete, requestUri, queryString, headers, cancellationToken);
        }

        public Task<Unit> DeleteWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Delete, requestUri, queryString, headers, cancellationToken);
        }

        public Task<TResponse> PostWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<Unit> PostWithCustomErrorAsync<TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<TResponse> PutWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Put, requestUri, requestData, headers, cancellationToken);
        }

        public Task<Unit> PutWithCustomErrorAsync<TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Put, requestUri, requestData, headers, cancellationToken);
        }

        public Task<TResponse> PatchWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Patch, requestUri, requestData, headers, cancellationToken);
        }

        public Task<Unit> PatchWithCustomErrorAsync<TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<TResponse> GetWithCustomErrorAsync<TResponse, TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<Unit> GetWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<string> GetStringWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<string, TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<Stream> GetStreamWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Stream, TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<byte[]> GetByteArrayWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<byte[], TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<TResponse> GetWithCustomErrorAsync<TResponse, TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<Unit> GetWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<string> GetStringWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<string, TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<Stream> GetStreamWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Stream, TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<byte[]> GetByteArrayWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<byte[], TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<TResponse> DeleteWithCustomErrorAsync<TResponse, TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Delete, requestUri, null, null, cancellationToken);
        }

        public Task<Unit> DeleteWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Delete, requestUri, null, null, cancellationToken);
        }

        public Task<TResponse> DeleteWithCustomErrorAsync<TResponse, TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Delete, requestUri, queryString, null, cancellationToken);
        }

        public Task<Unit> DeleteWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Delete, requestUri, queryString, null, cancellationToken);
        }

        public Task<TResponse> PostWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<Unit> PostWithCustomErrorAsync<TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<TResponse> PutWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Put, requestUri, requestData, null, cancellationToken);
        }

        public Task<Unit> PutWithCustomErrorAsync<TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Put, requestUri, requestData, null, cancellationToken);
        }

        public Task<TResponse> PatchWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TError>(HttpMethod.Patch, requestUri, requestData, null, cancellationToken);
        }

        public Task<Unit> PatchWithCustomErrorAsync<TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<TResponse> GetAsync<TResponse>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<Unit> GetAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<string> GetStringAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<string, TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<Stream> GetStreamAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Stream, TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<byte[]> GetByteArrayAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<byte[], TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<TResponse> GetAsync<TResponse>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<Unit> GetAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<string> GetStringAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<string, TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<Stream> GetStreamAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Stream, TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<byte[]> GetByteArrayAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<byte[], TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<TResponse> DeleteAsync<TResponse>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Delete, requestUri, null, headers, cancellationToken);
        }

        public Task<Unit> DeleteAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Delete, requestUri, null, headers, cancellationToken);
        }

        public Task<TResponse> DeleteAsync<TResponse>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Delete, requestUri, queryString, headers, cancellationToken);
        }

        public Task<Unit> DeleteAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Delete, requestUri, queryString, headers, cancellationToken);
        }

        public Task<TResponse> PostAsync<TResponse>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<Unit> PostAsync(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<TResponse> PutAsync<TResponse>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Put, requestUri, requestData, headers, cancellationToken);
        }

        public Task<Unit> PutAsync(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Put, requestUri, requestData, headers, cancellationToken);
        }

        public Task<TResponse> PatchAsync<TResponse>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Patch, requestUri, requestData, headers, cancellationToken);
        }

        public Task<Unit> PatchAsync(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<TResponse> GetAsync<TResponse>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<Unit> GetAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<string> GetStringAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<string, TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<Stream> GetStreamAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Stream, TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<byte[]> GetByteArrayAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<byte[], TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<TResponse> GetAsync<TResponse>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<Unit> GetAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<string> GetStringAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<string, TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<Stream> GetStreamAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Stream, TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<byte[]> GetByteArrayAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<byte[], TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<TResponse> DeleteAsync<TResponse>(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Delete, requestUri, null, null, cancellationToken);
        }

        public Task<Unit> DeleteAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Delete, requestUri, null, null, cancellationToken);
        }

        public Task<TResponse> DeleteAsync<TResponse>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Delete, requestUri, queryString, null, cancellationToken);
        }

        public Task<Unit> DeleteAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Delete, requestUri, queryString, null, cancellationToken);
        }

        public Task<TResponse> PostAsync<TResponse>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<Unit> PostAsync(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<TResponse> PutAsync<TResponse>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Put, requestUri, requestData, null, cancellationToken);
        }

        public Task<Unit> PutAsync(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Put, requestUri, requestData, null, cancellationToken);
        }

        public Task<TResponse> PatchAsync<TResponse>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<TResponse, TDefaultError>(HttpMethod.Patch, requestUri, requestData, null, cancellationToken);
        }

        public Task<Unit> PatchAsync(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.SendRequestAsync<Unit, TDefaultError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<(TResponse, TError)> TryGetWithCustomErrorAsync<TResponse, TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(Unit, TError)> TryGetWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(string, TError)> TryGetStringWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<string, TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(Stream, TError)> TryGetStreamWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Stream, TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(byte[], TError)> TryGetByteArrayWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<byte[], TError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(Unit, TError)> TryGetWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(string, TError)> TryGetStringWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<string, TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(Stream, TError)> TryGetStreamWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Stream, TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(byte[], TError)> TryGetByteArrayWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<byte[], TError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(TResponse, TError)> TryDeleteWithCustomErrorAsync<TResponse, TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Delete, requestUri, null, headers, cancellationToken);
        }

        public Task<(Unit, TError)> TryDeleteWithCustomErrorAsync<TError>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Delete, requestUri, null, headers, cancellationToken);
        }

        public Task<(TResponse, TError)> TryDeleteWithCustomErrorAsync<TResponse, TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Delete, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(Unit, TError)> TryDeleteWithCustomErrorAsync<TError>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Delete, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(TResponse, TError)> TryPostWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(Unit, TError)> TryPostWithCustomErrorAsync<TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(TResponse, TError)> TryPutWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Put, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(Unit, TError)> TryPutWithCustomErrorAsync<TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Put, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(TResponse, TError)> TryPatchWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Patch, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(Unit, TError)> TryPatchWithCustomErrorAsync<TError>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(TResponse, TError)> TryGetWithCustomErrorAsync<TResponse, TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(Unit, TError)> TryGetWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(string, TError)> TryGetStringWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<string, TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(Stream, TError)> TryGetStreamWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Stream, TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(byte[], TError)> TryGetByteArrayWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<byte[], TError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(TResponse, TError)> TryGetWithCustomErrorAsync<TResponse, TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(Unit, TError)> TryGetWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(string, TError)> TryGetStringWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<string, TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(Stream, TError)> TryGetStreamWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Stream, TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(byte[], TError)> TryGetByteArrayWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<byte[], TError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(TResponse, TError)> TryDeleteWithCustomErrorAsync<TResponse, TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Delete, requestUri, null, null, cancellationToken);
        }

        public Task<(Unit, TError)> TryDeleteWithCustomErrorAsync<TError>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Delete, requestUri, null, null, cancellationToken);
        }

        public Task<(TResponse, TError)> TryDeleteWithCustomErrorAsync<TResponse, TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Delete, requestUri, queryString, null, cancellationToken);
        }

        public Task<(Unit, TError)> TryDeleteWithCustomErrorAsync<TError>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Delete, requestUri, queryString, null, cancellationToken);
        }

        public Task<(TResponse, TError)> TryPostWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<(Unit, TError)> TryPostWithCustomErrorAsync<TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<(TResponse, TError)> TryPutWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Put, requestUri, requestData, null, cancellationToken);
        }

        public Task<(Unit, TError)> TryPutWithCustomErrorAsync<TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Put, requestUri, requestData, null, cancellationToken);
        }

        public Task<(TResponse, TError)> TryPatchWithCustomErrorAsync<TResponse, TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TError>(HttpMethod.Patch, requestUri, requestData, null, cancellationToken);
        }

        public Task<(Unit, TError)> TryPatchWithCustomErrorAsync<TError>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryGetAsync<TResponse>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryGetAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(string, TDefaultError)> TryGetStringAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<string, TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(Stream, TDefaultError)> TryGetStreamAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Stream, TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(byte[], TDefaultError)> TryGetByteArrayAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<byte[], TDefaultError>(HttpMethod.Get, requestUri, null, headers, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryGetAsync<TResponse>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryGetAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(string, TDefaultError)> TryGetStringAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<string, TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(Stream, TDefaultError)> TryGetStreamAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Stream, TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(byte[], TDefaultError)> TryGetByteArrayAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<byte[], TDefaultError>(HttpMethod.Get, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryDeleteAsync<TResponse>(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Delete, requestUri, null, headers, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryDeleteAsync(string requestUri, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Delete, requestUri, null, headers, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryDeleteAsync<TResponse>(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Delete, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryDeleteAsync(string requestUri, object queryString, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Delete, requestUri, queryString, headers, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryPostAsync<TResponse>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryPostAsync(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryPutAsync<TResponse>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Put, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryPutAsync(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Put, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryPatchAsync<TResponse>(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Patch, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryPatchAsync(string requestUri, object requestData, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Post, requestUri, requestData, headers, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryGetAsync<TResponse>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryGetAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(string, TDefaultError)> TryGetStringAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<string, TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(Stream, TDefaultError)> TryGetStreamAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Stream, TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(byte[], TDefaultError)> TryGetByteArrayAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<byte[], TDefaultError>(HttpMethod.Get, requestUri, null, null, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryGetAsync<TResponse>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryGetAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(string, TDefaultError)> TryGetStringAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<string, TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(Stream, TDefaultError)> TryGetStreamAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Stream, TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(byte[], TDefaultError)> TryGetByteArrayAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<byte[], TDefaultError>(HttpMethod.Get, requestUri, queryString, null, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryDeleteAsync<TResponse>(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Delete, requestUri, null, null, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryDeleteAsync(string requestUri, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Delete, requestUri, null, null, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryDeleteAsync<TResponse>(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Delete, requestUri, queryString, null, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryDeleteAsync(string requestUri, object queryString, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Delete, requestUri, queryString, null, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryPostAsync<TResponse>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryPostAsync(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryPutAsync<TResponse>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Put, requestUri, requestData, null, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryPutAsync(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Put, requestUri, requestData, null, cancellationToken);
        }

        public Task<(TResponse, TDefaultError)> TryPatchAsync<TResponse>(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<TResponse, TDefaultError>(HttpMethod.Patch, requestUri, requestData, null, cancellationToken);
        }

        public Task<(Unit, TDefaultError)> TryPatchAsync(string requestUri, object requestData, CancellationToken cancellationToken)
        {
            return this.TrySendRequestAsync<Unit, TDefaultError>(HttpMethod.Post, requestUri, requestData, null, cancellationToken);
        }

        /// <summary>
        /// Получает токен доступа, который отправляется с Bearer.
        /// </summary>
        /// <param name="cancellationToken">cancellationToken.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        protected virtual Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }

        protected virtual bool CheckResponseAsError(object response)
        {
            return false;
        }

        private static string ExtractMediaTypeFromHeaders(IDictionary<string, string> headers)
        {
            if (headers == null)
            {
                return null;
            }

            return headers.Remove("Content-Type", out var mediaType)
                ? mediaType
                : null;
        }

        private async Task<TResponse> SendWithHandlingExceptionsAsync<TResponse, TError>(
            HttpMethod httpMethod,
            string requestUri,
            object requestData,
            IDictionary<string, string> headers,
            CancellationToken cancellationToken)
        {
            var metricWriter = new RequestMetricWriter(this.metricsService, requestData, requestUri);
            var logsCollector = this.logsCollectorFactoryMethod(this.logger);
            var timeMetric = metricWriter.GetTimeMetric();
            var sw = new Stopwatch();
            sw.Start();
            try
            {
                try
                {
                    requestUri = this.GetRequestUrl<TResponse, TError>(requestUri);
                    if (requestData is not null && (httpMethod == HttpMethod.Get || httpMethod == HttpMethod.Delete))
                    {
                        logsCollector.AddLogsFromPayload(requestData);
                        requestUri += requestUri.IndexOf("?", StringComparison.Ordinal) > 0 ? "&" : "?";
                        requestUri += await this.SerializeObjectToQueryString(requestData);
                        requestData = null;
                    }

                    var request = new HttpRequestMessage(httpMethod, requestUri);
                    logsCollector.AddPath(requestUri);

                    var mediaType = ExtractMediaTypeFromHeaders(headers);
                    await this.AddHeadersAsync(request, headers, cancellationToken);
                    if (requestData != null)
                    {
                        var httpContent = await this.CreateHttpContent(requestData, mediaType ?? this.MediaType, logsCollector);
                        request.Content = httpContent;
                    }

                    logsCollector.AddRequestHeaders(request.Headers.ToDictionary(x => x.Key, y => string.Join(';', y.Value)));

                    using var httpClient = await this.CreateHttpClientAsync<TError>();
                    if (this.RequestTimeout > 0)
                    {
                        httpClient.Timeout = TimeSpan.FromMilliseconds(this.RequestTimeout);
                    }

                    var response = await httpClient.SendAsync(request, cancellationToken);

                    logsCollector.AddResponseHeaders(response.Headers.ToDictionary(x => x.Key, y => string.Join(';', y.Value)));
                    var deserializedResponse = await this.ReadResponseAsync<TResponse, TError>(response, logsCollector);
                    if (this.CheckResponseAsError(deserializedResponse))
                    {
                        await metricWriter.WriteMetricsAsError200(HttpStatusCode.OK).ConfigureAwait(false);
                    }
                    else
                    {
                        await metricWriter.WriteMetrics(response.StatusCode).ConfigureAwait(false);
                    }

                    sw.Stop();
                    return deserializedResponse;
                }
                catch (TaskCanceledException e) when (this.RequestTimeout > 0)
                {
                    sw.Stop();
                    throw new RequestException<TError>(e.Message, e, HttpStatusCode.GatewayTimeout);
                }
                catch (HttpRequestException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
                {
                    sw.Stop();
                    throw new RequestException<TError>(e.Message, e, HttpStatusCode.GatewayTimeout);
                }
                catch (HttpRequestException e) when (e.InnerException is SocketException se)
                {
                    sw.Stop();
                    throw new RequestException<TError>(e.Message, e, HttpStatusCode.BadGateway);
                }
                catch (DeserializeException e)
                {
                    sw.Stop();
                    throw new RequestException<TError>(e.Message, e, (int)e.StatusCode < 400 ? HttpStatusCode.BadRequest : e.StatusCode);
                }
                catch (RequestException<TError>)
                {
                    sw.Stop();
                    throw;
                }
                catch (Exception e)
                {
                    sw.Stop();
                    throw new RequestException<TError>("Failed request.", e, HttpStatusCode.InternalServerError);
                }
            }
            catch (RequestException<TError> e)
            {
                sw.Stop();
                await metricWriter.WriteMetrics(e.StatusCode ?? HttpStatusCode.InternalServerError)
                    .ConfigureAwait(false);
                logsCollector.AddStatus((int)e.StatusCode);
                throw;
            }
            finally
            {
                logsCollector.AddRequestDuration(sw.Elapsed.Milliseconds);
                logsCollector.WriteLogs();
                if (timeMetric != null)
                {
                    await timeMetric.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task<HttpClient> CreateHttpClientAsync<TError>()
        {
            if (this.httpClientFactory != null)
            {
                return this.httpClientFactory.Create();
            }

            if (!this.NeedDownloadCertificate)
            {
                return new HttpClient();
            }

            if (this.BaseUrl == null)
            {
                throw new RequestException<TError>("Failed download certificate. You must specify BaseUrl.", HttpStatusCode.BadRequest);
            }

            var domain = new Uri(this.BaseUrl).Host;
            var certificate = await this.DownloadCertificateAsync(domain);
            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                SslProtocols = SslProtocols.Tls12
            };
            handler.ClientCertificates.Add(certificate);
            return new HttpClient(handler);
        }

        private async Task<X509Certificate2> DownloadCertificateAsync(string domain)
        {
            using var client = new TcpClient(domain, 443);
            await using var sslStream = new SslStream(client.GetStream(), true, (_, _, _, _) => true);
            await sslStream.AuthenticateAsClientAsync(domain);
            return new X509Certificate2(sslStream.RemoteCertificate!);
        }

        private string GetRequestUrl<TResponse, TError>(string requestUri)
        {
            requestUri = requestUri?.IndexOf("http://") == 0 || requestUri?.IndexOf("https://") == 0
                ? requestUri
                : $"{this.BaseUrl}{requestUri}";

            var message = $"Failed url: {requestUri}.";
            if (requestUri.IndexOf("http") == -1 && string.IsNullOrEmpty(this.BaseUrl))
            {
                message += " Base URL not specified.";
            }

            return Helper.CheckUrlIsValid(requestUri) == false
                ? throw new RequestException<TError>(message, HttpStatusCode.BadRequest)
                : requestUri;
        }

        private async Task<HttpContent> CreateHttpContent(object requestData, string mediaType, RequestLogsCollector logsCollector)
        {
            if (requestData is byte[] requestDataAsBytes)
            {
                return this.CreateHttpContent(requestDataAsBytes);
            }

            if (requestData is MultipartFormData multipartFormData)
            {
                return this.CreateMultipartFormDataContent(multipartFormData);
            }

            logsCollector.AddLogsFromPayload(requestData);
            var serializedRequestData = await this.SerializeRequestData(requestData, mediaType);
            logsCollector.AddRequest(requestData);
            return new StringContent(serializedRequestData, Encoding.UTF8, mediaType);
        }

        private HttpContent CreateMultipartFormDataContent(MultipartFormData multipartFormData)
        {
            var content = new MultipartFormDataContent(this.DefaultBoundary);
            foreach (var item in multipartFormData)
            {
                var boundaryContent = item.Value is byte[] bytes
                    ? new ByteArrayContent(bytes)
                    : (HttpContent)new StringContent(this.SerializeObjectToJson(item.Value), item.Encoding, item.MediaType);
                boundaryContent.Headers.Add("Content-Type", item.MediaType);
                boundaryContent.Headers.Add("Content-Disposition", $"form-data; name=\"{item.Key}\"; filename=\"{item.FileName}\"");
                content.Add(boundaryContent);
            }

            content.Headers.Remove("Content-Type");
            content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={this.DefaultBoundary}");
            return content;
        }

        private async Task<string> SerializeRequestData(object requestData, string mediaType)
        {
            var serializedRequestData = mediaType switch
            {
                "application/x-www-form-urlencoded" => await this.SerializeObjectToQueryString(requestData),
                "text/xml" => this.SerializeObjectToXml(requestData),
                _ => this.SerializeObjectToJson(requestData)
            };
            return serializedRequestData;
        }

        private HttpContent CreateHttpContent(byte[] requestData)
        {
            var content = new ByteArrayContent(requestData);
            return content;
        }

        private async Task<TResponse> ReadResponseAsync<TResponse, TError>(HttpResponseMessage response, RequestLogsCollector logsCollector)
        {
            return !response.IsSuccessStatusCode
                ? throw new RequestException<TError>("Failed response.", await this.ReadResponseAsync<TError>(response, logsCollector), response.StatusCode, await response.Content.ReadAsStringAsync())
                : await this.ReadResponseAsync<TResponse>(response, logsCollector);
        }

        private async Task<TResponse> ReadResponseAsync<TResponse>(HttpResponseMessage response, RequestLogsCollector logsCollector)
        {
            logsCollector.AddStatus(response.IsSuccessStatusCode ? 200 : (int)response.StatusCode);

            if (typeof(TResponse) == typeof(Unit))
            {
                return default;
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            if (typeof(TResponse) == typeof(Stream))
            {
                var stream = await response.Content.ReadAsStreamAsync();
                return (TResponse)((object)stream);
            }

            if (typeof(TResponse) == typeof(byte[]))
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return (TResponse)((object)bytes);
            }

            var responseAsString = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseAsString))
            {
                return default;
            }

            if (typeof(TResponse) == typeof(string))
            {
                if (responseAsString.StartsWith('"'))
                {
                    responseAsString = responseAsString.Trim('"');
                }

                logsCollector.AddResponse(responseAsString);
                return (TResponse)((object)responseAsString);
            }

            var deserializedResponse = this.DeserializeResponse<TResponse>(response, responseAsString);
            logsCollector.AddResponse(deserializedResponse);
            if (deserializedResponse is IHasStatusCode responseWithStatusCode)
            {
                responseWithStatusCode.StatusCode = (int)response.StatusCode;
            }

            logsCollector.AddLogsFromPayload(deserializedResponse);

            return deserializedResponse;
        }

        private TResponse DeserializeResponse<TResponse>(HttpResponseMessage response, string responseAsString)
        {
            try
            {
                var mediaType = response.Content?.Headers?.ContentType?.MediaType ?? this.MediaType;
                var isXml = mediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase);
                var responseAsObj = isXml
                    ? this.DeserializeObjectFromXml<TResponse>(responseAsString)
                    : this.DeserializeObjectFromJson<TResponse>(responseAsString);
                return responseAsObj;
            }
            catch (Exception e)
            {
                throw new DeserializeException(e, responseAsString, response.StatusCode);
            }
        }

        private bool IsNumericOrBool(Type type)
        {
            return type.IsPrimitive || type == typeof(decimal);
        }

        private bool IsValueTypeOrString(Type type)
        {
            return type.IsValueType || type == typeof(string);
        }

        private TResponse DeserializeObjectFromJson<TResponse>(string json)
        {
            if (!this.IsNumericOrBool(typeof(TResponse)) && this.IsValueTypeOrString(typeof(TResponse)) && !json.StartsWith('"'))
            {
                json = '"' + json + '"';
            }

            return json.Deserialize<TResponse>(this.DeserializeAdditionalConverters);
        }

        private TResponse DeserializeObjectFromXml<TResponse>(string xml)
        {
            var serializer = new XmlSerializer(typeof(TResponse));
            using TextReader reader = new StringReader(xml);
            return (TResponse)serializer.Deserialize(reader);
        }

        private string SerializeObjectToJson(object obj)
        {
            if (obj is string json)
            {
                return json;
            }

            return obj.Serialize(this.SerializeAdditionalConverters, null, this.IsCamelCase);
        }

        private async Task<string> SerializeObjectToQueryString(object obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            if (this.IsValueTypeOrString(obj.GetType()))
            {
                return obj as string;
            }

            var objAsJson = obj.Serialize(this.SerializeAdditionalConverters, null, this.IsCamelCase);
            return await QueryStringMapper.MapToQueryString(JToken.Parse(objAsJson));
        }

        private string SerializeObjectToXml(object obj)
        {
            var serializer = new XmlSerializer(obj.GetType());
            using var stream = new MemoryStream();
            var xmlNamespaces = new XmlSerializerNamespaces();
            xmlNamespaces.Add(string.Empty, string.Empty);
            serializer.Serialize(stream, obj, xmlNamespaces);
            var content = Encoding.UTF8.GetString(stream.ToArray());
            content = new Regex("^<\\?xml version=\"1.0\"\\?>").Replace(
                content,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            return content;
        }

        private async Task AddHeadersAsync(HttpRequestMessage request, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            var accessToken = await this.GetAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            if (headers != null)
            {
                foreach (var (key, value) in headers)
                {
                    request.Headers.Add(key, value);
                }
            }
        }

        private async Task<TResponse> DurableRequestAsync<TResponse, TError>(Func<Task<TResponse>> func, CancellationToken cancellationToken)
        {
            short currentAttempts = 1;

            while (true)
            {
                try
                {
                    return await func().ConfigureAwait(false);
                }
                catch (RequestException<TError> e) when ((int)e.StatusCode >= this.DurableFromHttpStatus)
                {
                    this.logger?.LogError(e, "Error");

                    currentAttempts++;

                    if (currentAttempts > this.Attempts)
                    {
                        this.logger?.LogDebug("Limit of attempts is exceeded");

                        throw;
                    }

                    await Task.Delay(this.MillisecondsDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private RequestLogsCollector CreateRequestLogsCollector(ILogger loggerInstance = null)
        {
            return new RequestLogsCollector(loggerInstance);
        }
    }
}
