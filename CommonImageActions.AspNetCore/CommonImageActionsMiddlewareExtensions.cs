using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace CommonImageActions.AspNetCore
{
    public static class CommonImageActionsMiddlewareExtensions
    {
        public static IApplicationBuilder UseCommonImageActions(
            this IApplicationBuilder builder, CommonImageActionSettings? settings)
        {
            IOptions<CommonImageActionSettings> options;
            if (settings == null)
            {
                options = Options.Create(new CommonImageActionSettings());
            }
            else
            {
                options = Options.Create(settings);
            }
            return builder.UseMiddleware<CommonImageActionsMiddleware>(options);
        }
    }
}
