using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AgentHelm.Web.Components;

public partial class Dock
{
    [Inject] public IJSRuntime JS { get; set; } = default!;
    [Parameter] public bool Open { get; set; }
    [Parameter] public string Tab { get; set; } = "changes";
    [Parameter] public EventCallback OnToggle { get; set; }
    [Parameter] public EventCallback<string> OnSelectTab { get; set; }
    [Parameter] public RenderFragment? ChangesContent { get; set; }
    [Parameter] public RenderFragment? TerminalContent { get; set; }

    private ElementReference _dockEl;
    private ElementReference _splitterEl;
    private bool _attached;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!Open)
        {
            _attached = false;
            return;
        }
        if (_attached) return;
        _attached = true;
        try
        {
            await JS.InvokeVoidAsync("helmDock.attach", _dockEl, _splitterEl);
        }
        catch (JSException) { }
    }
}
