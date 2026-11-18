using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Tests.Validation;

public sealed class ValidatorTests
{
    private static readonly SendGiftValidator Validator = new();

    [Fact]
    public void Valid_instance_has_no_failures()
    {
        var command = new SendGift("Leyla", "leyla@example.az", 1500, new Recipient("ana@example.az"), "Doğum günün mübarək! ə ğ ı ö ü ç ş");

        Assert.Empty(Validator.Validate(command));
        Assert.True(Validator.ValidateToResult(command).IsSuccess);
    }

    [Fact]
    public void Each_invalid_field_reports_camel_case_path_and_stable_code()
    {
        var command = new SendGift(" ", "not-an-email", 10, new Recipient("x@"), new string('a', 501));

        var failures = Validator.Validate(command);

        Assert.Equal(
            [
                ("senderName", ValidationCodes.Required),
                ("senderEmail", ValidationCodes.InvalidEmail),
                ("amountQepik", ValidationCodes.OutOfRange),
                ("recipient.email", ValidationCodes.InvalidEmail),
                ("message", ValidationCodes.TooLong),
            ],
            failures.Select(f => (f.Field, f.Code)));
    }

    [Fact]
    public void Only_first_failing_check_per_field_is_reported()
    {
        var command = new SendGift(null, "a@b.az", 1500, new Recipient("ana@b.az"), null);

        var failure = Assert.Single(Validator.Validate(command));

        Assert.Equal(("senderName", ValidationCodes.Required), (failure.Field, failure.Code));
    }

    [Fact]
    public void Conditional_and_object_rules()
    {
        var anonymousWithoutName = new SendGift("Leyla", "a@b.az", 1500, new Recipient("ana@b.az"), null, Anonymous: true);
        var selfGift = new SendGift("Leyla", "a@b.az", 1500, new Recipient("A@B.az"), null);

        Assert.Empty(Validator.Validate(anonymousWithoutName with { SenderName = null }));
        Assert.Equal("self_gift", Assert.Single(Validator.Validate(selfGift)).Code);
    }

    [Theory]
    [InlineData("a@b.az", true)]
    [InlineData("first.last+tag@sub.example.az", true)]
    [InlineData("a@b", false)]
    [InlineData("a@@b.az", false)]
    [InlineData("a b@c.az", false)]
    [InlineData("@b.az", false)]
    [InlineData("a@b..az", false)]
    public void Email_rule(string email, bool valid)
    {
        var command = new SendGift("Leyla", email, 1500, new Recipient("ana@example.az"), null);

        Assert.Equal(valid, !Validator.Validate(command).Any(f => f.Field == "senderEmail"));
    }

    [Fact]
    public void Validation_error_becomes_problem_details_with_field_errors()
    {
        var result = Validator.ValidateToResult(new SendGift(" ", "a@b.az", 1500, new Recipient("x@y.az"), null));

        var problem = result.Error.ToProblemDetails();
        var json = JsonSerializer.Serialize(problem.Extensions["errors"], new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.Equal(400, problem.Status);
        Assert.Equal("validation_failed", problem.Extensions["code"]);
        Assert.Equal("[{\"field\":\"senderName\",\"code\":\"required\",\"message\":\"Boş ola bilməz.\"}]", json);
    }

    [Theory]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Failure, 422)]
    public void Error_types_map_to_status_codes(ErrorType type, int status)
    {
        var problem = Error.Create("gift_expired", "Hədiyyənin vaxtı bitib.", type).ToProblemDetails();

        Assert.Equal(status, problem.Status);
        Assert.Equal("gift_expired", problem.Extensions["code"]);
        Assert.False(problem.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public void Data_annotations_are_mapped_to_the_same_codes()
    {
        var validator = new DataAnnotationsValidator<PartnerApplication>();

        var failures = validator.Validate(new PartnerApplication { Name = null, Email = "bad", Voen = "123", Branches = 0 });

        Assert.Equal(
            [("name", ValidationCodes.Required), ("email", ValidationCodes.InvalidEmail), ("voen", ValidationCodes.InvalidFormat), ("branches", ValidationCodes.OutOfRange)],
            failures.Select(f => (f.Field, f.Code)));
        Assert.Empty(validator.Validate(new PartnerApplication { Name = "Çayxana", Email = "a@b.az", Voen = "1234567890", Branches = 2 }));
    }

    [Fact]
    public void Validation_error_requires_failures()
    {
        Assert.Throws<ArgumentException>(() => new ValidationError([]));
    }

    public sealed record Recipient(string? Email);

    public sealed record SendGift(string? SenderName, string? SenderEmail, long AmountQepik, Recipient Recipient, string? Message, bool Anonymous = false);

    private sealed class SendGiftValidator : Validator<SendGift>
    {
        public SendGiftValidator()
        {
            RuleFor(x => x.SenderName).NotEmpty().MaxLength(100).When(x => !x.Anonymous);
            RuleFor(x => x.SenderEmail).NotEmpty().Email();
            RuleFor(x => x.AmountQepik).InRange(200, 20_000);
            RuleFor(x => x.Recipient.Email).NotEmpty().Email();
            RuleFor(x => x.Message).MaxLength(500);
            Rule("recipient.email", x => !string.Equals(x.SenderEmail, x.Recipient.Email, StringComparison.OrdinalIgnoreCase), "self_gift", "Özünüzə hədiyyə göndərə bilməzsiniz.");
        }
    }

    public sealed class PartnerApplication
    {
        [Required]
        public string? Name { get; init; }

        [EmailAddress]
        public string? Email { get; init; }

        [RegularExpression("^[0-9]{10}$")]
        public string? Voen { get; init; }

        [Range(1, 500)]
        public int Branches { get; init; }
    }
}
