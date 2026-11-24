using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AIQueryGenerator.API.Infrastructure;

/// <summary>Runs the FluentValidation validator registered for each action argument before the action executes.</summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid)
                throw new RequestValidationException(result.Errors.Select(e => new ApiError(ToCamel(e.PropertyName), e.ErrorMessage)).ToList());
        }

        await next();
    }

    private static string ToCamel(string name) => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
