using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Rex.Tests.Support;

/// <summary>
/// Drives the real window through UI Automation, the way assistive tools do. WPF exposes every
/// x:Name as an AutomationId and every text as a Name, so the tests speak in the app's own terms.
///
/// Requests run on the calling test thread on purpose. UI Automation's client side is bound to the
/// apartment that created it, so handing calls to a worker thread makes every request marshal
/// across apartments and sit there; the boundedness that stops a wedged window hanging CI comes
/// from the per-test timeout instead, which abandons the test and lets the rest of the run finish.
/// </summary>
public sealed class AppAutomation(IntPtr window)
{
    /// <summary>How long an element may take to appear after the action that should create it.</summary>
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

    public AutomationElement Find(string automationId) =>
        Wait(() => FindBy(AutomationElement.AutomationIdProperty, automationId), $"element '{automationId}'");

    public AutomationElement FindByName(string name) =>
        Wait(() => FindBy(AutomationElement.NameProperty, name), $"element named '{name}'");

    public bool Exists(string automationId) =>
        Bounded(() => FindBy(AutomationElement.AutomationIdProperty, automationId), $"look for '{automationId}'") is not null;

    public void Invoke(string automationId) =>
        Act(automationId, e => Pattern<InvokePattern>(e, InvokePattern.Pattern, automationId).Invoke(), "invoke");

    public void InvokeNamed(string name) =>
        Bounded(() => { Pattern<InvokePattern>(FindByName(name), InvokePattern.Pattern, name).Invoke(); return true; }, $"invoke '{name}'");

    public void Select(string automationId) =>
        Act(automationId, e => Pattern<SelectionItemPattern>(e, SelectionItemPattern.Pattern, automationId).Select(), "select");

    public void SetValue(string automationId, double value) =>
        Act(automationId, e => Pattern<RangeValuePattern>(e, RangeValuePattern.Pattern, automationId).SetValue(value), "set the value of");

    public void SetText(string automationId, string text) =>
        Act(automationId, e => Pattern<ValuePattern>(e, ValuePattern.Pattern, automationId).SetValue(text), "type into");

    public void Expand(AutomationElement element) =>
        Bounded(() => { Pattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, "element").Expand(); return true; }, "expand an element");

    public int CountChildren(string automationId)
    {
        var element = Find(automationId);
        return Bounded(() => element.FindAll(TreeScope.Children, Condition.TrueCondition).Count, $"count the children of '{automationId}'");
    }

    /// <summary>
    /// Toggles until the control reports the wanted state. UI Automation returns before WPF has
    /// processed the click, so each attempt waits for the state to settle instead of toggling twice.
    /// </summary>
    public void Toggle(string automationId, bool on)
    {
        var element = Find(automationId);
        var pattern = Pattern<TogglePattern>(element, TogglePattern.Pattern, automationId);
        var wanted = on ? ToggleState.On : ToggleState.Off;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (Settles(pattern, wanted, automationId, attempt == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(3)))
            {
                return;
            }

            Bounded(() => { pattern.Toggle(); return true; }, $"toggle '{automationId}'");
        }

        Assert.True(Settles(pattern, wanted, automationId, TimeSpan.FromSeconds(3)), $"'{automationId}' never reached {wanted}.");
    }

    /// <summary>
    /// Expands a settings group. A collapsed WPF Expander never realises its content, so nothing
    /// inside it exists for automation (or for the user) until the group is open.
    /// </summary>
    public void ExpandGroup(string automationId)
    {
        var element = Find(automationId);
        var pattern = Pattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, automationId);
        if (Bounded(() => pattern.Current.ExpandCollapseState, $"read the state of '{automationId}'") != ExpandCollapseState.Expanded)
        {
            Bounded(() => { pattern.Expand(); return true; }, $"expand '{automationId}'");
        }

        Wait(
            () => Bounded(() => pattern.Current.ExpandCollapseState, $"read the state of '{automationId}'") == ExpandCollapseState.Expanded ? element : null,
            $"'{automationId}' to open");
    }

    public void SelectComboItem(string comboId, string itemName)
    {
        var combo = Find(comboId);
        var expand = Pattern<ExpandCollapsePattern>(combo, ExpandCollapsePattern.Pattern, comboId);
        Bounded(() => { expand.Expand(); return true; }, $"open '{comboId}'");
        var item = Wait(
            () => Bounded(() => combo.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, itemName)), $"look for '{itemName}'"),
            $"item '{itemName}' in '{comboId}'");
        Bounded(() => { Pattern<SelectionItemPattern>(item, SelectionItemPattern.Pattern, itemName).Select(); return true; }, $"select '{itemName}'");
        Bounded(() => { expand.Collapse(); return true; }, $"close '{comboId}'");
    }

    private AutomationElement? FindBy(AutomationProperty property, string value) =>
        AutomationElement.FromHandle(window).FindFirst(TreeScope.Descendants, new PropertyCondition(property, value));

    private void Act(string automationId, Action<AutomationElement> action, string verb)
    {
        var element = Find(automationId);
        Bounded(() => { action(element); return true; }, $"{verb} '{automationId}'");
    }

    private static T Pattern<T>(AutomationElement element, AutomationPattern pattern, string description) where T : BasePattern =>
        Bounded(() => (T)element.GetCurrentPattern(pattern), $"read the {typeof(T).Name} of '{description}'");

    private bool Settles(TogglePattern pattern, ToggleState wanted, string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (Bounded(() => pattern.Current.ToggleState, $"read the state of '{automationId}'") == wanted)
            {
                return true;
            }

            TestContext.Current.CancellationToken.WaitHandle.WaitOne(Poll);
        }
        while (DateTime.UtcNow < deadline && !TestContext.Current.CancellationToken.IsCancellationRequested);

        return false;
    }

    private static AutomationElement Wait(Func<AutomationElement?> find, string description)
    {
        var deadline = DateTime.UtcNow + FindTimeout;
        while (true)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            if (Bounded(find, $"find {description}") is { } element)
            {
                return element;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"UI Automation could not find {description}.");
            }

            TestContext.Current.CancellationToken.WaitHandle.WaitOne(Poll);
        }
    }

    /// <summary>
    /// Runs one UI Automation request. The description is kept so a failure says what the test was
    /// doing rather than surfacing a bare COM error from somewhere inside the automation client.
    /// </summary>
    private static T Bounded<T>(Func<T> call, string description)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            throw new InvalidOperationException($"UI Automation could not {description}: {ex.Message}", ex);
        }
    }
}
