# Validation in FastEndpoints

FastEndpoints integrates **FluentValidation** as a first-class feature with automatic assembly scanning and automatic short-circuiting.

---

## 1. Defining a Validator

Create a class inheriting from `Validator<TRequest>`. Place it in the same file or folder as the corresponding request and endpoint:

```csharp
using FastEndpoints;
using FluentValidation;

namespace MyApp.Features.Users.RegisterUser;

public sealed class RegisterUserRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required int Age { get; init; }
}

public sealed class RegisterUserValidator : Validator<RegisterUserRequest>
{
    public RegisterUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one number.");

        RuleFor(x => x.Age)
            .InclusiveBetween(18, 120).WithMessage("User must be at least 18 years old.");
    }
}
```

---

## 2. Automatic Failure Interception

- FastEndpoints automatically discovers any class inheriting from `Validator<TRequest>`.
- By default, validation runs before `HandleAsync()` executes.
- If any validation rule fails, request execution is halted, and FastEndpoints automatically returns an RFC 7807 **HTTP 400 Problem Details** response containing all validation error messages and property names.

### Default 400 Problem Details Payload:
```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "email": [
      "A valid email address is required."
    ],
    "password": [
      "Password must be at least 8 characters long."
    ]
  }
}
```

---

## 3. In-Handler Programmatic Validation

When business rules require database lookups or external state checks inside `HandleAsync()`, add errors programmatically using built-in error accumulation methods:

```csharp
public override async Task HandleAsync(RegisterUserRequest req, CancellationToken ct)
{
    // Check database for existing email
    bool emailExists = await _userRepository.ExistsByEmailAsync(req.Email, ct);
    if (emailExists)
    {
        // 1. Add error associated with a specific property
        AddError(r => r.Email, "Email address is already registered.");
    }

    // 2. Check general condition
    if (IsSystemInMaintenanceMode())
    {
        // Add general non-property error
        AddError("Registrations are temporarily paused for maintenance.");
    }

    // 3. Halt execution and send 400 response if any errors were accumulated
    ThrowIfAnyErrors();

    // Or throw an immediate single error directly:
    // ThrowError("Immediate failure message");

    // Proceed if all checks pass
    var user = await _userRepository.CreateAsync(req, ct);
    await SendOkAsync(new RegisterUserResponse { UserId = user.Id }, ct);
}
```

---

## 4. Disabling Auto-Validation (Manual Control)

If an endpoint needs to inspect validation failures manually without automatic short-circuiting:

```csharp
public override void Configure()
{
    Post("/orders/draft");
    DontThrowIfValidationFails(); // Disables auto-interception
}

public override async Task HandleAsync(CreateDraftOrderRequest req, CancellationToken ct)
{
    if (ValidationFailed)
    {
        // Access collected failures
        var failureMessages = ValidationFailures.Select(f => f.ErrorMessage);
        // Custom partial handling
        return;
    }

    await SendOkAsync(new DraftResponse(), ct);
}
```

---

## 5. Dependency Injection in Validators

Validators are registered as singletons by default. Avoid injecting scoped services (such as `DbContext`) into validator constructors, as this causes captive dependency bugs.

Instead:
1. Keep the `Validator<TRequest>` pure (syntax, lengths, regex, presence, ranges).
2. Perform asynchronous database checks (uniqueness, foreign key existence) inside the endpoint's `HandleAsync()` using `AddError()` and `ThrowIfAnyErrors()`.
