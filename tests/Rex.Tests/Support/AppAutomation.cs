using System.Windows.Automation;

namespace Rex.Tests.Support;

/// <summary>
/// Drives the real window through UI Automation, the way assistive tools do. WPF exposes every
/// x:Name as an AutomationId and every text as a Name, so the tests speak in the app's own terms.
/// </summary>
public sealed class AppAutomation(IntPtr window)
{
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(8);

    private AutomationElement Root => AutomationElement.FromHandle(window);

    public AutomationElement Find(string automationId) =>
        Wait(() => Root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)), $"element '{automationId}'");

    public AutomationElement FindByName(string name) =>
        Wait(() => Root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)), $"element named '{name}'");

    public bool Exists(string automationId) =>
        Root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)) is not null;

    public void Invoke(string automationId) => Pattern<InvokePattern>(Find(automationId), InvokePattern.Pattern).Invoke();

    public void InvokeNamed(string name) => Pattern<InvokePattern>(FindByName(name), InvokePattern.Pattern).Invoke();

    /// <summary>
    /// Toggles until the control reports the wanted state. UI Automation returns before WPF has
    /// processed the click, so each attempt waits for the state to settle instead of toggling twice.
    /// </summary>
    public void Toggle(string automationId, bool on)
    {
        var pattern = Pattern<TogglePattern>(Find(automationId), TogglePattern.Pattern);
        var wanted = on ? ToggleState.On : ToggleState.Off;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (Settles(pattern, wanted, attempt == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(3)))
            {
                return;
            }

            pattern.Toggle();
        }

        Assert.True(Settles(pattern, wanted, TimeSpan.FromSeconds(3)), $"'{automationId}' never reached {wanted}.");
    }

    private static bool Settles(TogglePattern pattern, ToggleState wanted, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            if (pattern.Current.ToggleState == wanted)
            {
                return true;
            }

            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    public void Select(string automationId) => Pattern<SelectionItemPattern>(Find(automationId), SelectionItemPattern.Pattern).Select();

    public void SetValue(string automationId, double value) => Pattern<RangeValuePattern>(Find(automationId), RangeValuePattern.Pattern).SetValue(value);

    public void SetText(string automationId, string text) => Pattern<ValuePattern>(Find(automationId), ValuePattern.Pattern).SetValue(text);

    public void Expand(AutomationElement element) => Pattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern).Expand();

    /// <summary>
    /// Expands a settings group. A collapsed WPF Expander never realises its content, so nothing
    /// inside it exists for automation (or for the user) until the group is open.
    /// </summary>
    public void ExpandGroup(string automationId)
    {
        var pattern = Pattern<ExpandCollapsePattern>(Find(automationId), ExpandCollapsePattern.Pattern);
        if (pattern.Current.ExpandCollapseState != ExpandCollapseState.Expanded)
        {
            pattern.Expand();
        }

        Thread.Sleep(150);
    }

    public void SelectComboItem(string comboId, string itemName)
    {
        var combo = Find(comboId);
        var expand = Pattern<ExpandCollapsePattern>(combo, ExpandCollapsePattern.Pattern);
        expand.Expand();
        var item = Wait(() => combo.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, itemName)), $"item '{itemName}' in '{comboId}'");
        Pattern<SelectionItemPattern>(item, SelectionItemPattern.Pattern).Select();
        expand.Collapse();
    }

    public int CountChildren(string automationId) => Find(automationId).FindAll(TreeScope.Children, Condition.TrueCondition).Count;

    private static T Pattern<T>(AutomationElement element, AutomationPattern pattern) where T : BasePattern =>
        (T)element.GetCurrentPattern(pattern);

    private static AutomationElement Wait(Func<AutomationElement?> find, string description)
    {
        var deadline = DateTime.UtcNow + FindTimeout;
        while (true)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            if (find() is { } element)
            {
                return element;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"UI Automation could not find {description}.");
            }

            Thread.Sleep(100);
        }
    }
}
