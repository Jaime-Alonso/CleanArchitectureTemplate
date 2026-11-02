using CleanTemplate.Domain.Entities;
using FluentValidation;

namespace CleanTemplate.Application.Products.Commands.UpdateProduct;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(Product.NameMaxLength).WithMessage($"Name must not exceed {Product.NameMaxLength} characters");

        RuleFor(x => x.Description)
            .MaximumLength(Product.DescriptionMaxLength);

        RuleFor(x => x.Price)
            .Must(Product.IsValidPrice).WithMessage("Price exceeds the supported range or scale");

        RuleFor(x => x.Stock)
            .GreaterThanOrEqualTo(0).WithMessage("Stock cannot be negative");
    }
}
