namespace NotifyRail.Api.Features.Messages.CreateMessage;

public static class CreateMessageRequestNormalizer
{
    public static CreateMessageNormalizationResult Normalize(CreateMessageRequest request)
    {
        if (request.Type is not ("otp" or "transactional" or "campaign"))
        {
            return CreateMessageNormalizationResult.Failure(
                "type must be one of: otp, transactional, campaign");
        }

        if (request.Channel != "sms")
        {
            return CreateMessageNormalizationResult.Failure("channel must be sms");
        }

        var senderTitle = request.SenderTitle?.Trim();
        if (string.IsNullOrEmpty(senderTitle))
        {
            return CreateMessageNormalizationResult.Failure("sender_title is required");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return CreateMessageNormalizationResult.Failure("body is required");
        }

        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            return CreateMessageNormalizationResult.Failure("idempotency_key is required");
        }

        if (request.Recipients is null || request.Recipients.Count == 0)
        {
            return CreateMessageNormalizationResult.Failure("at least one recipient is required");
        }

        var recipients = new List<string>(request.Recipients.Count);
        var seenRecipients = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < request.Recipients.Count; index++)
        {
            var recipient = request.Recipients[index]?.Trim();
            if (string.IsNullOrEmpty(recipient))
            {
                return CreateMessageNormalizationResult.Failure(
                    $"recipients[{index}] must not be empty");
            }

            if (!seenRecipients.Add(recipient))
            {
                return CreateMessageNormalizationResult.Failure(
                    $"recipient {recipient} must not be repeated");
            }

            recipients.Add(recipient);
        }

        if (request.Encoding is not null and not ("latin" or "turkish" or "unicode"))
        {
            return CreateMessageNormalizationResult.Failure(
                "encoding must be one of: latin, turkish, unicode");
        }

        var reportLabel = request.ReportLabel?.Trim();

        return CreateMessageNormalizationResult.Success(new CreateMessageCommand(
            request.Type,
            request.Channel,
            senderTitle,
            request.Body,
            recipients,
            idempotencyKey,
            request.ScheduledAt,
            reportLabel,
            request.Encoding));
    }
}
