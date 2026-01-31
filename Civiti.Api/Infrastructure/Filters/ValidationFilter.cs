using FluentValidation;
using FluentValidation.Results;

namespace Civiti.Api.Infrastructure.Filters;

/// <summary>
/// Endpoint filter that validates request bodies using FluentValidation.
/// Resolves IValidator&lt;T&gt; from DI and returns 400 with errors if validation fails.
/// </summary>
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        IValidator<T>? validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is null)
        {
            return await next(context);
        }

        T? argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return await next(context);
        }

        ValidationResult? result = await validator.ValidateAsync(argument);

        if (result.IsValid)
        {
            return await next(context);
        }

        Dictionary<string, string[]> errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        return Results.ValidationProblem(errors);
    }
}