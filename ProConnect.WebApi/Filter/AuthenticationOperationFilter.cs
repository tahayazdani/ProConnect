using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.WebApi.Filter
{
    public class AuthenticationOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var isAuthorized = context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any();
            var allowAnonymous = context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any(); 

            if (isAuthorized && !allowAnonymous)
            {
                if (operation.Security == null)
                    operation.Security = new List<OpenApiSecurityRequirement>();
                var scheme = new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearer" } };
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [scheme] = new List<string>()
                });
            }
        }
    }
}
