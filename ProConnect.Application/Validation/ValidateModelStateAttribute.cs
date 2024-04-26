using FluentValidation;
using FluentValidation.AspNetCore;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Application.Validation
{
    public class ValidateModelStateAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.IsValid)
            {
                var errors = context.ModelState.Values.Where(v => v.Errors.Count > 0)
                        .SelectMany(v => v.Errors)
                        .Select(v => v.ErrorMessage)
                        .ToList();

                var responseObj = JsonConvert.SerializeObject(new ProConnectResponse<string>
                {
                    body = string.Empty,
                    statuscode = 500,
                    errormessage = "Validation",
                    message = $"Something went wrong, Please call administrator.",
                    issuccessfull = false,
                    errors = errors
                }).ToString();

                context.Result = new OkObjectResult(responseObj)
                {
                    StatusCode = 500
                };
            }
        }
    }



}
