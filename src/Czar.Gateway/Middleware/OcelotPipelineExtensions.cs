﻿using System;
using System.Threading.Tasks;
using Czar.Gateway.Authentication.Middleware;
using Czar.Gateway.RateLimit.Middleware;
using Czar.Gateway.Requester.Middleware;
using Czar.Gateway.Responder.Middleware;
using Ocelot.Authentication.Middleware;
using Ocelot.Authorisation.Middleware;
using Ocelot.Cache.Middleware;
using Ocelot.DownstreamRouteFinder.Middleware;
using Ocelot.DownstreamUrlCreator.Middleware;
using Ocelot.Errors.Middleware;
using Ocelot.Headers.Middleware;
using Ocelot.LoadBalancer.Middleware;
using Ocelot.Middleware;
using Ocelot.Middleware.Pipeline;
using Ocelot.RateLimit.Middleware;
using Ocelot.Request.Middleware;
using Ocelot.Requester.Middleware;
using Ocelot.RequestId.Middleware;
using Ocelot.Responder.Middleware;
using Ocelot.WebSockets.Middleware;

namespace Czar.Gateway.Middleware
{
    /// <summary>
    /// 金焰的世界
    /// 2018-11-15
    /// 网关管道扩展
    /// </summary>
    public static class OcelotPipelineExtensions
    {
        public static OcelotRequestDelegate BuildCzarOcelotPipeline(this IOcelotPipelineBuilder builder,
            OcelotPipelineConfiguration pipelineConfiguration)
        {
            // This is registered to catch any global exceptions that are not handled
            // It also sets the Request Id if anything is set globally
            builder.UseExceptionHandlerMiddleware();

            // If the request is for websockets upgrade we fork into a different pipeline
            builder.MapWhen(context => context.HttpContext.WebSockets.IsWebSocketRequest,
                app =>
                {
                    app.UseDownstreamRouteFinderMiddleware();
                    app.UseDownstreamRequestInitialiser();
                    app.UseLoadBalancingMiddleware();
                    app.UseDownstreamUrlCreatorMiddleware();
                    app.UseWebSocketsProxyMiddleware();
                });

            // Allow the user to respond with absolutely anything they want.
            builder.UseIfNotNull(pipelineConfiguration.PreErrorResponderMiddleware);

            //builder.UseResponderMiddleware();
            builder.UseCzarResponderMiddleware();

            // Then we get the downstream route information
            builder.UseDownstreamRouteFinderMiddleware();

            //Expand other branch pipes
            if (pipelineConfiguration.MapWhenOcelotPipeline != null)
            {
                foreach (var pipeline in pipelineConfiguration.MapWhenOcelotPipeline)
                {
                    builder.MapWhen(pipeline);
                }
            }

            // Now we have the ds route we can transform headers and stuff?
            builder.UseHttpHeadersTransformationMiddleware();

            // Initialises downstream request
            builder.UseDownstreamRequestInitialiser();

            // We check whether the request is ratelimit, and if there is no continue processing
            builder.UseRateLimiting();

            // This adds or updates the request id (initally we try and set this based on global config in the error handling middleware)
            // If anything was set at global level and we have a different setting at re route level the global stuff will be overwritten
            // This means you can get a scenario where you have a different request id from the first piece of middleware to the request id middleware.
            builder.UseRequestIdMiddleware();

            // Allow pre authentication logic. The idea being people might want to run something custom before what is built in.
            builder.UseIfNotNull(pipelineConfiguration.PreAuthenticationMiddleware);

            // Now we know where the client is going to go we can authenticate them.
            // We allow the ocelot middleware to be overriden by whatever the
            // user wants
            if (pipelineConfiguration.AuthenticationMiddleware == null)
            {
                builder.UseAuthenticationMiddleware();
            }
            else
            {
                builder.Use(pipelineConfiguration.AuthenticationMiddleware);
            }

            //添加自定义限流中间件 2018-11-18 金焰的世界
            builder.UseCzarClientRateLimitMiddleware();

            //添加自定义授权中间件  2018-11-15 金焰的世界
            builder.UseAhphAuthenticationMiddleware();

            // Allow pre authorisation logic. The idea being people might want to run something custom before what is built in.
            builder.UseIfNotNull(pipelineConfiguration.PreAuthorisationMiddleware);

            // Now we have authenticated and done any claims transformation we 
            // can authorise the request
            // We allow the ocelot middleware to be overriden by whatever the
            // user wants
            if (pipelineConfiguration.AuthorisationMiddleware == null)
            {
                builder.UseAuthorisationMiddleware();
            }
            else
            {
                builder.Use(pipelineConfiguration.AuthorisationMiddleware);
            }

            // Allow the user to implement their own query string manipulation logic
            builder.UseIfNotNull(pipelineConfiguration.PreQueryStringBuilderMiddleware);

            // Get the load balancer for this request
            builder.UseLoadBalancingMiddleware();

            // This takes the downstream route we retrieved earlier and replaces any placeholders with the variables that should be used
            builder.UseDownstreamUrlCreatorMiddleware();

            // Not sure if this is the best place for this but we use the downstream url 
            // as the basis for our cache key.
            builder.UseOutputCacheMiddleware();

            //We fire off the request and set the response on the scoped data repo
            //builder.UseHttpRequesterMiddleware();
            builder.UseCzaHttpRequesterMiddleware();
            return builder.Build();
        }

        private static void UseIfNotNull(this IOcelotPipelineBuilder builder,
            Func<DownstreamContext, Func<Task>, Task> middleware)
        {
            if (middleware != null)
            {
                builder.Use(middleware);
            }
        }
    }
}
