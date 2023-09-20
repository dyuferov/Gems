// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System.Net.Http;

namespace Gems.Http
{
    public interface IHttpClientFactory
    {
        HttpClient Create();
    }
}
