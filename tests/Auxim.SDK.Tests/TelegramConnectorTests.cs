using System.Text.Json;
using Xunit;

namespace Auxim.SDK.Tests;

public sealed class TelegramConnectorTests
{
    [Fact]
    public void TelegramJson_DeserializesSnakeCaseUpdates()
    {
        const string json = """
            {
              "update_id": 42,
              "message": {
                "message_id": 7,
                "from": { "id": 123, "is_bot": false, "first_name": "Ada" },
                "chat": { "id": 456, "type": "private" },
                "text": "hello",
                "message_thread_id": 9
              }
            }
            """;

        var update = JsonSerializer.Deserialize<TelegramUpdate>(json, TelegramJson.Options);

        Assert.NotNull(update);
        Assert.Equal(42, update.UpdateId);
        Assert.Equal(7, update.Message?.MessageId);
        Assert.Equal(123, update.Message?.From?.Id);
        Assert.Equal(9, update.Message?.MessageThreadId);
    }
}
