using CleanTemplate.Domain.Entities;
using FluentValidation;

namespace CleanTemplate.Application.Products.Commands.CreateProduct;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
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
