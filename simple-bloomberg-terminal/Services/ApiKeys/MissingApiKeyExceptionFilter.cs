using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace simple_bloomberg_terminal.Services.ApiKeys;

/// <summary>
/// Turns a <see cref="MissingApiKeyException"/> escaping an action into an HTTP 424 (Failed
/// Dependency) carrying {code:"MISSING_KEY", provider, message}. site.js intercepts that status and
/// shows the "add your key" popup. Full-page form actions catch the exception themselves (to add a
/// ModelState error) before it reaches here, so this only fires for AJAX/JSON endpoints.
/// Note: streaming endpoints validate the key BEFORE writing the response â€” once the body has
/// started this filter can no longer set a result.
/// </summary>
public class MissingApiKeyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not MissingApiKeyException ex) return;

        context.Result = new ObjectResult(new
        {
            code = "MISSING_KEY",
            provider = ex.Provider,
            message = ex.Message
        })
        {
            StatusCode = StatusCodes.Status424FailedDependency
        };
        context.ExceptionHandled = true;
    }
}
