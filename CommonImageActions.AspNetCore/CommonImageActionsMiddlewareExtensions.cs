using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace CommonImageActions.AspNetCore
{
    public static class CommonImageActionsMiddlewareExtensions
    {
        /// <summary>
        /// Add the image middleware. When no settings are given here, the settings registered
        /// with <see cref="AddCommonImageActions"/> (or bound from configuration) are used.
        /// </summary>
        public static IApplicationBuilder UseCommonImageActions(
            this IApplicationBuilder builder, CommonImageActionSettings settings = null)
        {
            IOptions<CommonImageActionSettings> options;
            if (settings == null)
            {
                //use what was registered in the service collection, if anything was
                options = builder.ApplicationServices?.GetService(typeof(IOptions<CommonImageActionSettings>)) as IOptions<CommonImageActionSettings>
                    ?? Options.Create(new CommonImageActionSettings());
            }
            else
            {
                options = Options.Create(settings);
            }
            return builder.UseMiddleware<CommonImageActionsMiddleware>(options);
        }

        /// <summary>
        /// Register the settings that <c>app.UseCommonImageActions()</c> uses when it is called
        /// without any. This is optional, settings can also be passed straight to UseCommonImageActions.
        /// </summary>
        public static IServiceCollection AddCommonImageActions(
            this IServiceCollection services, Action<CommonImageActionSettings> configure = null)
        {
            services.AddOptions();
            if (configure != null)
            {
                services.Configure(configure);
            }
            return services;
        }
    }
}
