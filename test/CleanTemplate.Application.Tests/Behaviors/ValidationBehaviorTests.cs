using CleanTemplate.Application.Behaviors;
using CleanTemplate.Application.Extensions;
using CleanTemplate.SharedKernel.Errors;
using CleanTemplate.SharedKernel.Results;
using FluentValidation;
using Mediora;
using Microsoft.Extensions.DependencyInjection;

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
            () => Task.FromResult(Result<Guid>.Success(Guid.NewGuid())),
            CancellationToken.None);

        Assert.True(response.IsFailure);
        Assert.Contains(response.Errors, error => error.Code == "Validation.Name");
        Assert.Contains(response.Errors, error => error.Type == ErrorType.Validation);
        Assert.Throws<InvalidOperationException>(() => response.Value);
    }

    [Fact]
    public async Task Handle_Result_AggregatesFailuresAndDoesNotCallHandler()
    {
        var behavior = new ValidationBehavior<PlainCommand, Result>([new PlainValidator("first"), new PlainValidator("second")]);
        var called = false;
        var result = await behavior.Handle(new PlainCommand(), () => { called = true; return Task.FromResult(Result.Success()); }, CancellationToken.None);
        Assert.False(called);
        Assert.Equal(new[] { "first", "second" }, result.Errors.Select(e => e.Description));
        Assert.All(result.Errors, e => Assert.Equal(ErrorType.Validation, e.Type));
    }

    [Fact]
    public async Task Handle_ValidatorsSharingScopedDependency_RunSequentially()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedProbe>();
        services.AddScoped<IValidator<PlainCommand>, FirstScopedValidator>();
        services.AddScoped<IValidator<PlainCommand>, SecondScopedValidator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<ScopedProbe>();
        var behavior = new ValidationBehavior<PlainCommand, Result>(scope.ServiceProvider.GetServices<IValidator<PlainCommand>>());
        var called = false;
        var result = await behavior.Handle(new PlainCommand(), () => { called = true; return Task.FromResult(Result.Success()); }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.True(called);
        Assert.Equal(2, probe.Calls);
        Assert.False(probe.Overlap);
    }

    [Fact]
    public async Task IncompatibleResponse_WithoutValidator_IsAllowed()
    {
        var behavior = new ValidationBehavior<OtherCommand, string>([]);
        Assert.Equal("ok", await behavior.Handle(new OtherCommand(), () => Task.FromResult("ok"), CancellationToken.None));
    }

    [Fact]
    public void IncompatibleResponse_WithValidator_IsRejectedDuringConfigurationAndConstruction()
    {
        var services = new ServiceCollection();
        services.AddScoped<IValidator<OtherCommand>, OtherValidator>();
        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateRequestValidationConfiguration());
        Assert.Contains(nameof(OtherCommand), exception.Message);
        Assert.Contains("Result", exception.Message);
        Assert.Throws<InvalidOperationException>(() => new ValidationBehavior<OtherCommand, string>([new OtherValidator()]));
    }

    [Fact]
    public void FinalConfigurationCheck_RejectsLateFactoryValidatorWithoutInstantiatingIt()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        var instantiated = false;
        services.AddScoped<IValidator<OtherCommand>>(_ =>
        {
            instantiated = true;
            return new OtherValidator();
        });

        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateRequestValidationConfiguration());

        Assert.Contains(nameof(OtherCommand), exception.Message);
        Assert.Contains("Result", exception.Message);
        Assert.False(instantiated);
    }

    public sealed record PlainCommand : IRequest<Result>;
    public sealed record OtherCommand : IRequest<string>;
    public sealed class OtherValidator : AbstractValidator<OtherCommand> { }

    private sealed class PlainValidator : AbstractValidator<PlainCommand>
    {
        public PlainValidator(string message) => RuleFor(x => x).Must(_ => false).WithMessage(message);
    }

    public sealed class ScopedProbe
    {
        private int _active;
        public int Calls { get; private set; }
        public bool Overlap { get; private set; }
        public async Task<bool> CheckAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _active) != 1) Overlap = true;
            await Task.Delay(20, cancellationToken);
            Calls++;
            Interlocked.Decrement(ref _active);
            return true;
        }
    }

    public sealed class FirstScopedValidator : AbstractValidator<PlainCommand>
    {
        public FirstScopedValidator(ScopedProbe probe) => RuleFor(x => x).MustAsync((_, ct) => probe.CheckAsync(ct));
    }

    public sealed class SecondScopedValidator : AbstractValidator<PlainCommand>
    {
        public SecondScopedValidator(ScopedProbe probe) => RuleFor(x => x).MustAsync((_, ct) => probe.CheckAsync(ct));
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
