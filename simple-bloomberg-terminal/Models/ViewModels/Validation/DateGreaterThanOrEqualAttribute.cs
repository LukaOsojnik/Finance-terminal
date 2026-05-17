using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace simple_bloomberg_terminal.Models.ViewModels.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class DateGreaterThanOrEqualAttribute(string otherProperty) : ValidationAttribute, IClientModelValidator
{
    public string OtherProperty { get; } = otherProperty;

    protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
    {
        if (value is null) return ValidationResult.Success;
        var prop = ctx.ObjectType.GetProperty(OtherProperty);
        if (prop is null) return ValidationResult.Success;
        var otherRaw = prop.GetValue(ctx.ObjectInstance);
        if (otherRaw is null) return ValidationResult.Success;

        if (value is DateOnly endD && otherRaw is DateOnly startD && endD < startD)
            return new ValidationResult(ErrorMessage ?? $"Must be on or after {OtherProperty}.",
                new[] { ctx.MemberName ?? string.Empty });

        if (value is DateTime endT && otherRaw is DateTime startT && endT < startT)
            return new ValidationResult(ErrorMessage ?? $"Must be on or after {OtherProperty}.",
                new[] { ctx.MemberName ?? string.Empty });

        return ValidationResult.Success;
    }

    public void AddValidation(ClientModelValidationContext ctx)
    {
        ctx.Attributes.TryAdd("data-val", "true");
        ctx.Attributes.TryAdd("data-val-dategte", ErrorMessage ?? $"Must be on or after {OtherProperty}.");
        ctx.Attributes.TryAdd("data-val-dategte-other", OtherProperty);
    }
}
