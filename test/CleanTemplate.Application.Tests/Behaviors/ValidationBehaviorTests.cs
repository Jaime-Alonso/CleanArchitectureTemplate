using CleanTemplate.Application.Behaviors;
using CleanTemplate.Core.SharedKernel.Errors;
using CleanTemplate.Core.SharedKernel.Results;
using FluentValidation;
using MediatR;

namespace CleanTemplate.Application.Tests.Behaviors;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WhenValidationFails_ReturnsFailureResultWithErrors()
    {
        var validators = new IValidator<TestCommand>[] { new TestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommand, Result<Guid>>(validators);

        var response = await behavior.Handle(
            new TestCommand { Name = string.Empty },
            _ => Task.FromResult(Result<Guid>.Success(Guid.NewGuid())),
            CancellationToken.None);

        Assert.True(response.IsFailure);
        Assert.Contains(response.Errors, error => error.Code == "Validation.Name");
        Assert.Contains(response.Errors, error => error.Type == ErrorType.Validation);
    }

    public sealed record TestCommand : IRequest<Result<Guid>>
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class TestCommandValidator : AbstractValidator<TestCommand>
    {
        public TestCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }
}
