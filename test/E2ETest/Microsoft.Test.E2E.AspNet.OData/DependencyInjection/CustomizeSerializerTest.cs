﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dispatcher;
using Microsoft.AspNet.OData;
using Microsoft.AspNet.OData.Extensions;
using Microsoft.AspNet.OData.Formatter.Serialization;
using Microsoft.AspNet.OData.Routing.Conventions;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.Test.E2E.AspNet.OData.Common;
using Microsoft.Test.E2E.AspNet.OData.Common.Execution;
using Xunit;

namespace Microsoft.Test.E2E.AspNet.OData.DependencyInjection
{
    public class CustomizeSerializerTest : WebHostTestBase
    {
        private const string CustomerBaseUrl = "{0}/customserializer/Customers";

        public CustomizeSerializerTest(WebHostTestFixture fixture)
            :base(fixture)
        {
        }

        protected override void UpdateConfiguration(HttpConfiguration configuration)
        {
            configuration.Services.Replace(
                typeof(IAssembliesResolver),
                new TestAssemblyResolver(typeof(CustomersController), typeof(OrdersController)));
            configuration.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
            configuration.Formatters.JsonFormatter.SerializerSettings.ReferenceLoopHandling =
                Newtonsoft.Json.ReferenceLoopHandling.Ignore;
            configuration.MapODataServiceRoute("customserializer", "customserializer", builder =>
                builder.AddService(ServiceLifetime.Singleton, sp => EdmModel.GetEdmModel())
                       .AddService<IEnumerable<IODataRoutingConvention>>(ServiceLifetime.Singleton, sp =>
                           ODataRoutingConventions.CreateDefaultWithAttributeRouting("customserializer", configuration))
                       .AddService<ODataSerializerProvider, MyODataSerializerProvider>(ServiceLifetime.Singleton)
                       .AddService<ODataResourceSerializer, AnnotatingEntitySerializer>(ServiceLifetime.Singleton));
        }

        /// <remarks>
        /// This test is failing due to ODataSerializerProviderProxy.Instance and
        /// ODataDeserializerProviderProxy.Instance, which are internal classes (not access to
        /// them here) and they create a static instance. Those instances cache the service collection
        /// from an earlier test so when this is run with other tests, the container with MyODataSerializerProvider
        /// and AnnotatingEntitySerializer is never inspected. They test pass fine as long as they are not
        /// run with another test.
        /// TODO: https://github.com/OData/WebApi/issues/1228
        /// </remarks>
        [Fact(Skip ="See remark")]
        public async Task CutomizeSerializerProvider()
        {
            string queryUrl =
                string.Format(
                    CustomerBaseUrl + "/Default.EnumFunction()",
                    BaseAddress);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json;odata.metadata=none"));
            HttpClient client = new HttpClient();

            HttpResponseMessage response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains(MyODataSerializerProvider.EnumNotSupportError, result);
        }

        [Fact(Skip = "See remark")]
        public async Task CutomizeSerializer()
        {
            string queryUrl =
                string.Format(
                    CustomerBaseUrl + "(1)",
                    BaseAddress);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json;odata.metadata=none"));
            request.Headers.Add("Prefer", "odata.include-annotations=\"*\"");
            HttpClient client = new HttpClient();

            HttpResponseMessage response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();
            Assert.Contains("@dependency.injection.test\":1", result);
        }
    }

    public class MyODataSerializerProvider : DefaultODataSerializerProvider
    {
        public const string EnumNotSupportError = "Enum kind is not support.";

        public MyODataSerializerProvider(IServiceProvider rootContainer)
            : base(rootContainer)
        {
        }

        public override ODataEdmTypeSerializer GetEdmTypeSerializer(IEdmTypeReference edmType)
        {
            if (edmType.TypeKind() == EdmTypeKind.Enum)
            {
                throw new ArgumentException(EnumNotSupportError);
            }

            return base.GetEdmTypeSerializer(edmType);
        }
    }

    // A custom entity serializer that adds the score annotation to document entries.
    public class AnnotatingEntitySerializer : ODataResourceSerializer
    {
        public AnnotatingEntitySerializer(ODataSerializerProvider serializerProvider)
            : base(serializerProvider)
        {
        }

        public override ODataResource CreateResource(SelectExpandNode selectExpandNode, ResourceContext resourceContext)
        {
            ODataResource resource = base.CreateResource(selectExpandNode, resourceContext);
            Customer customer = resourceContext.ResourceInstance as Customer;
            if (customer != null)
            {
                resource.InstanceAnnotations.Add(new ODataInstanceAnnotation("dependency.injection.test",
                    new ODataPrimitiveValue(customer.Id)));
            }
            return resource;
        }
    }
}