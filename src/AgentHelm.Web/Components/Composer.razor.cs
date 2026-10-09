using AgentHelm.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

namespace AgentHelm.Web.Components;

public partial class Composer
{
    [Parameter] public string Prompt { get; set; } = "";
    [Parameter] public EventCallback<string> PromptChanged { get; set; }
    [Parameter] public bool Running { get; set; }
    [Parameter] public IReadOnlyList<AttachmentDto> Attachments { get; set; } = [];
    [Parameter] public EventCallback<AttachmentDto> OnRemoveAttachment { get; set; }
    [Parameter] public EventCallback<InputFileChangeEventArgs> OnFilesSelected { get; set; }
    [Parameter] public EventCallback OnSend { get; set; }
    [Parameter] public string? AttachError { get; set; }
    [Parameter] public string? SendError { get; set; }
    [Parameter] public string AttachTitle { get; set; } = "";
    [Parameter] public string Policy { get; set; } = "ask";
    [Parameter] public int PolicySelectVersion { get; set; }
    [Parameter] public EventCallback<ChangeEventArgs> OnPolicyChanged { get; set; }
    [Parameter] public string? Model { get; set; }

    private Task PromptInputAsync(ChangeEventArgs e) => PromptChanged.InvokeAsync(e.Value?.ToString() ?? "");

    private async Task KeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.CtrlKey) await OnSend.InvokeAsync();
    }
}
