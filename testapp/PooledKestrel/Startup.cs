// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Test.Perf.WebFx.Apps.LowAlloc
{
    public class Startup
    {
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");
        private static IConfiguration _configuration;

        public void ConfigureServices(IServiceCollection services)
        {
            services.Remove(services.FirstOrDefault(s => s.ServiceType == typeof(IStartupFilter)));
            services.AddSingleton(typeof(IHttpContextFactory), typeof(PooledHttpContextFactory));
            services.AddSingleton(typeof(IConfiguration), _configuration);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.Run(context =>
            {
                var response = context.Response;
                response.StatusCode = 200;
                response.ContentType = "text/plain";
                // HACK: Setting the Content-Length header manually avoids the cost of serializing the int to a string.
                //       This is instead of: httpContext.Response.ContentLength = _helloWorldPayload.Length;
                response.Headers["Content-Length"] = "13";
                return response.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldPayload.Length);
            });
        }

        public static void Main(string[] args)
        {
            _configuration = new ConfigurationBuilder()
                .AddCommandLine(args)
                .AddJsonFile("hosting.json", optional: true)
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            var host = new WebHostBuilder()
                .UseKestrel()
                .UseConfiguration(_configuration)
                .UseIISIntegration()
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
    }
}
