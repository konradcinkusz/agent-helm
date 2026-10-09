using System.Text.Json;
using AgentHelm.Bridge.Persistence;
using AgentHelm.Bridge.Sessions;
using Xunit;

namespace AgentHelm.Tests;

public class QuickChatTests
{
    [Fact]
    public void ChatDirectoryIsCreatedPerSession()
    {
        var first = SessionManager.CreateChatDirectory(Guid.NewGuid().ToString("N"));
        var second = SessionManager.CreateChatDirectory(Guid.NewGuid().ToString("N"));

        Assert.True(Directory.Exists(first));
        Assert.NotEqual(first, second);
        Assert.StartsWith(Path.GetTempPath(), first);
        Directory.Delete(first);
        Directory.Delete(second);
    }

    [Fact]
    public void SnapshotWithoutIsChatLoadsAsProjectSession()
    {
        const string legacy = """
            {"Id":"a1","AgentId":"echo","Cwd":"/repo","Title":"t","CreatedAt":"2026-01-01T00:00:00Z",
             "LastActivity":"2026-01-01T00:00:00Z","Transcript":[]}
            """;

        var archived = JsonSerializer.Deserialize<ArchivedSession>(legacy);
        Assert.False(archived!.IsChat);

        var chat = JsonSerializer.Deserialize<ArchivedSession>(
            JsonSerializer.Serialize(archived with { IsChat = true }));
        Assert.True(chat!.IsChat);
    }
}
