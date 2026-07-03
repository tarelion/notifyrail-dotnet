using NotifyRail.Api.Features.Messages.CreateMessage;

namespace NotifyRail.Api.Tests;

public sealed class CreateMessageRequestNormalizerTests
{
    [Fact]
    public void Normalize_TrimsRequestFields()
    {
        var request = ValidRequest() with
        {
            SenderTitle = " NotifyRail ",
            Recipients = [" +905551111111 ", "+905552222222 "],
            IdempotencyKey = " order-42-ready ",
            ReportLabel = " Shipping Updates ",
        };

        var result = CreateMessageRequestNormalizer.Normalize(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Command);
        Assert.Equal("NotifyRail", result.Command.SenderTitle);
        Assert.Equal(["+905551111111", "+905552222222"], result.Command.Recipients);
        Assert.Equal("order-42-ready", result.Command.IdempotencyKey);
        Assert.Equal("Shipping Updates", result.Command.ReportLabel);
    }

    [Fact]
    public void Normalize_RejectsDuplicateRecipientsAfterTrim()
    {
        var request = ValidRequest() with
        {
            Recipients = [" +905551111111 ", "+905551111111"],
        };

        var result = CreateMessageRequestNormalizer.Normalize(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("recipient +905551111111 must not be repeated", result.Error);
    }

    [Theory]
    [InlineData("email", "type must be one of: otp, transactional, campaign")]
    [InlineData("transactional", null)]
    public void Normalize_ValidatesMessageType(string type, string? expectedError)
    {
        var request = ValidRequest() with
        {
            Type = type,
        };

        var result = CreateMessageRequestNormalizer.Normalize(request);

        Assert.Equal(expectedError is null, result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
    }

    private static CreateMessageRequest ValidRequest()
    {
        return new CreateMessageRequest(
            Type: "transactional",
            Channel: "sms",
            SenderTitle: "NotifyRail",
            Body: "Your order is ready.",
            Recipients: ["+905551111111"],
            IdempotencyKey: "order-42-ready");
    }
}
