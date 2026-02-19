using FluentValidation;

namespace CleanTemplate.Application.Security.Commands.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.EmailOrUserName)
            .NotEmpty().WithMessage("Email or username is required")
            .MaximumLength(256).WithMessage("Email or username must not exceed 256 characters");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MaximumLength(256).WithMessage("Password must not exceed 256 characters");
    }
}
