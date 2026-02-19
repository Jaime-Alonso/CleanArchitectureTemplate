using FluentValidation;

namespace CleanTemplate.Application.Security.Commands.RefreshToken;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required")
            .MaximumLength(2048).WithMessage("Refresh token must not exceed 2048 characters");
    }
}
