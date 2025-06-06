﻿using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace CommonImageActions.AspNetCore
{
    public static class CommonImageActionsMiddlewareExtensions
    {
        public static IApplicationBuilder UseCommonImageActions(
            this IApplicationBuilder builder, CommonImageActionSettings settings = null)
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
