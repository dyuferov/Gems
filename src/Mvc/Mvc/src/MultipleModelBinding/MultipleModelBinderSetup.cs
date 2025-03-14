// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System.Linq;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Options;

namespace Gems.Mvc.MultipleModelBinding
{
    public class MultipleModelBinderSetup : IPostConfigureOptions<MvcOptions>
    {
        public void PostConfigure(string name, MvcOptions options)
        {
            var bodyProvider = options.ModelBinderProviders.Single(provider => provider.GetType() == typeof(BodyModelBinderProvider));
#if NETCOREAPP3_1
            var complexProvider = options.ModelBinderProviders.Single(provider => provider.GetType() == typeof(ComplexTypeModelBinderProvider));
#else
            var complexProvider = options.ModelBinderProviders.Single(provider => provider.GetType() == typeof(ComplexObjectModelBinderProvider));
#endif
            var multipleModelBinderProvider = new MultipleModelBinderProvider(bodyProvider, complexProvider);

            options.ModelBinderProviders.Insert(0, multipleModelBinderProvider);
        }
    }
}
