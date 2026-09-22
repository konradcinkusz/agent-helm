// EchoAgent — a tiny ACP-speaking agent for demos and manual testing.
// Lets AgentHelm run end-to-end with ZERO real agents installed:
//   prompt → streamed chunks → (optionally) a tool call with a permission ask.
// Say the word "tool" in your prompt to trigger the permission flow.
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

var stdin = Console.In;
var stdout = Console.Out;
var rnd = new Random();

// Prompts run off the read loop (see session/prompt below), so writes can come
// from several tasks at once; one line must never interleave with another.
var sendLock = new SemaphoreSlim(1, 1);

async Task Send(JsonNode node)
{
    await sendLock.WaitAsync();
    try
    {
        await stdout.WriteLineAsync(node.ToJsonString());
        await stdout.FlushAsync();
    }
    finally { sendLock.Release(); }
}

async Task Notify(string sessionId, JsonObject update)
{
    await Send(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["method"] = "session/update",
        ["params"] = new JsonObject { ["sessionId"] = sessionId, ["update"] = update }
    });
}

long serverReqId = 1000;
var pendingServerRequests = new ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>>();

async Task HandlePromptAsync(JsonNode msg, JsonNode? id)
{
    var sessionId = msg["params"]?["sessionId"]?.GetValue<string>() ?? "echo";
    var text = msg["params"]?["prompt"]?[0]?["text"]?.GetValue<string>() ?? "";
    var blockCount = (msg["params"]?["prompt"] as JsonArray)?.Count ?? 1;
    var attachNote = blockCount > 1 ? $" [+{blockCount - 1} attachment(s) received]" : "";

    // Stream the echo in a few chunks, like a real agent would.
    foreach (var chunk in new[] { "Echo agent here. ", "You said: ", $"\"{text}\"{attachNote}" })
    {
        await Notify(sessionId, new JsonObject
        {
            ["sessionUpdate"] = "agent_message_chunk",
            ["content"] = new JsonObject { ["type"] = "text", ["text"] = chunk }
        });
        await Task.Delay(150);
    }

    // Optional tool + permission exercise.
    if (text.Contains("tool", StringComparison.OrdinalIgnoreCase))
    {
        await Notify(sessionId, new JsonObject
        {
            ["sessionUpdate"] = "tool_call",
            ["toolCallId"] = "demo-1",
            ["title"] = "write_demo_file",
            ["kind"] = "edit",
            ["status"] = "pending"
        });

        var reqId = Interlocked.Increment(ref serverReqId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingServerRequests[reqId] = tcs;
        await Send(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = reqId,
            ["method"] = "session/request_permission",
            ["params"] = new JsonObject
            {
                ["sessionId"] = sessionId,
                ["toolCall"] = new JsonObject { ["title"] = "write_demo_file", ["kind"] = "edit" },
                ["options"] = new JsonArray(
                    new JsonObject { ["optionId"] = "allow", ["name"] = "Allow", ["kind"] = "allow_once" },
                    new JsonObject { ["optionId"] = "reject", ["name"] = "Reject", ["kind"] = "reject_once" })
            }
        });
        // Completed by the read loop when the client's answer arrives.
        var outcome = await tcs.Task;
        var granted = outcome?["outcome"]?["optionId"]?.GetValue<string>() == "allow";

        await Notify(sessionId, new JsonObject
        {
            ["sessionUpdate"] = "tool_call_update",
            ["toolCallId"] = "demo-1",
            ["status"] = granted ? "completed" : "failed"
        });
        await Notify(sessionId, new JsonObject
        {
            ["sessionUpdate"] = "agent_message_chunk",
            ["content"] = new JsonObject
            {
                ["type"] = "text",
                ["text"] = granted ? " Tool ran with your blessing." : " Tool was rejected — respecting that."
            }
        });
    }

    await Send(new JsonObject
    {
        ["jsonrpc"] = "2.0", ["id"] = id,
        ["result"] = new JsonObject { ["stopReason"] = "end_turn" }
    });
}

while (await stdin.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonNode? msg;
    try { msg = JsonNode.Parse(line); } catch { continue; }
    if (msg is null) continue;

    // Responses to OUR requests (e.g. permission outcome)
    if (msg["id"] is { } respId && msg["method"] is null)
    {
        if (pendingServerRequests.TryRemove(respId.GetValue<long>(), out var tcs))
            tcs.TrySetResult(msg["result"]);
        continue;
    }

    var method = msg["method"]?.GetValue<string>();
    var id = msg["id"]?.DeepClone();

    switch (method)
    {
        case "initialize":
            await Send(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = id,
                ["result"] = new JsonObject
                {
                    ["protocolVersion"] = 1,
                    ["agentCapabilities"] = new JsonObject
                    {
                        ["loadSession"] = true,
                        ["promptCapabilities"] = new JsonObject { ["image"] = true, ["embeddedContext"] = true }
                    }
                }
            });
            break;

        case "session/new":
            await Send(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = id,
                ["result"] = new JsonObject { ["sessionId"] = $"echo-{rnd.Next(1000, 9999)}" }
            });
            break;

        case "session/prompt":
            // Off the read loop: a prompt that asks for permission waits for the
            // client's answer, and only this loop can read that answer. Awaiting
            // the prompt here would deadlock the turn.
            _ = Task.Run(() => HandlePromptAsync(msg, id));
            break;

        case "session/load":
        {
            // Demo resume: replay a short "history" then confirm the load.
            var sessionId = msg["params"]?["sessionId"]?.GetValue<string>() ?? "echo";
            await Notify(sessionId, new JsonObject
            {
                ["sessionUpdate"] = "agent_message_chunk",
                ["content"] = new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"[resumed session {sessionId} — echo agent remembers you]"
                }
            });
            await Send(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = id,
                ["result"] = null
            });
            break;
        }

        case "session/cancel":
            break; // notification, nothing to answer

        default:
            if (id is not null)
                await Send(new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = id,
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" }
                });
            break;
    }
}
