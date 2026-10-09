using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Rex.Core;
using Rex.Mirror.Views.Controls;
using Rex.Mirror.Views.Settings;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The Shortcuts from anywhere group without a window: its rows, its switch, and what it says about a key.</summary>
public sealed class GlobalKeysPanelTests
{
    private static GlobalKeysGroup Group(RexConfig config)
    {
        var group = Wpf.Layout(new GlobalKeysGroup(), 360);
        group.Refresh(config);
        return group;
    }

    [Fact]
    public void TheShortcutsFromAnywhereGroupIsComplete()
    {
        Wpf.Run(() =>
        {
            var config = new RexConfig();
            var group = Group(config);
            Assert.True(group.GlobalOptions.IsEnabled);
            Assert.Equal("Ctrl+Alt+M", group.GlobalShowHide.Text);
            Assert.Equal("Works from anywhere.", group.GlobalShowHideNote.Text);
            Assert.All(Wpf.Logical(group).OfType<Control>().Where(c => c is ComboBox or CheckBox or ChordBox or Button),
                control => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), control.Name));

            // Off: every row stays in sight, cannot be changed, and says why.
            config.GlobalKeys.Enabled = false;
            group.Refresh(config);
            Assert.False(group.GlobalOptions.IsEnabled);
            Assert.StartsWith("Turn on", AutomationProperties.GetHelpText(group.GlobalShowHide), StringComparison.Ordinal);

            // Twenty actions is the most; the add button says so.
            config.GlobalKeys.Enabled = true;
            config.GlobalKeys.Actions = [.. Enumerable.Range(0, GlobalKeysSettings.MostActions)
                .Select(n => new GlobalKeyAction { Key = "Ctrl+Shift+" + (char)('A' + n), Action = MirrorActions.Ids[n] })];
            config.Normalize();
            group.Refresh(config);
            Assert.Equal(GlobalKeysSettings.MostActions, group.GlobalActions.Children.Count);
            Assert.False(group.GlobalAddAction.IsEnabled);
            Assert.Contains("the most there can be", group.GlobalActionsNote.Text, StringComparison.Ordinal);
            Assert.Equal(string.Empty, AutomationProperties.GetHelpText(group.GlobalShowHide));
            // An action that has a key is not offered again.
            Assert.DoesNotContain(group.GlobalNewAction.Items.OfType<ComboBoxItem>(), item => (string)item.Tag == MirrorActions.Ids[0]);
        });
    }

    [Fact]
    public void TheLayoutWarningSaysWhichCharacter()
    {
        Wpf.Run(() =>
        {
            var group = Group(new RexConfig());
            group.CharacterFor = chord => chord.Key == 'M' && chord.Alt ? "µ" : null;
            group.TakenElsewhere = chord => chord.Key == 'J';

            group.OnShowHideChosen("Ctrl+Alt+M");
            Assert.Equal("On your keyboard this also types 'µ'.", group.GlobalShowHideNote.Text);

            group.OnShowHideChosen("Ctrl+Shift+J");
            Assert.Equal("Another app uses this key too. This app takes it while it runs.", group.GlobalShowHideNote.Text);
            Assert.Equal("Ctrl+Shift+J", group.GlobalShowHide.Chord);

            // A key the window uses for something else is refused, and the box keeps what was there.
            group.OnShowHideChosen("Ctrl+Alt+S");
            Assert.Equal("In this window the key already means: Save a screenshot.", group.GlobalShowHideNote.Text);
            Assert.Equal("Ctrl+Shift+J", group.GlobalShowHide.Chord);

            // Turned off, it says where the key goes instead.
            group.GlobalShowHideOff.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Off", group.GlobalShowHide.Text);
            Assert.Equal("Off: the key reaches the app in front.", group.GlobalShowHideNote.Text);
            Assert.False(group.GlobalShowHideOff.IsEnabled);
        });
    }

    [Fact]
    public void AnActionIsAddedGivenAKeyAndRemoved()
    {
        Wpf.Run(() =>
        {
            var config = new RexConfig();
            var group = Group(config);
            group.GlobalNewAction.SelectedItem = group.GlobalNewAction.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == "screenshot");
            group.GlobalAddAction.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // Waiting for its key: listed, not saved yet.
            var box = Assert.IsType<ChordBox>(group.FindName("GlobalActionKey_screenshot"));
            Assert.Equal("Off", box.Text);
            Assert.Empty(config.GlobalKeys.Actions);
            Assert.Equal("Press the keys for it.", ((TextBlock)((Grid)group.GlobalActions.Children[0]).Children[3]).Text);

            group.OnActionChosen("screenshot", "Alt+Q");
            Assert.Contains("needs Ctrl", ((TextBlock)((Grid)group.GlobalActions.Children[0]).Children[3]).Text, StringComparison.Ordinal);
            Assert.Empty(config.GlobalKeys.Actions);

            group.OnActionChosen("screenshot", "Ctrl+Shift+F10");
            Assert.Equal("Ctrl+Shift+F10=screenshot", string.Join(',', config.GlobalKeys.Actions.Select(a => a.Key + "=" + a.Action)));
            Assert.Equal("Ctrl+Shift+F10", Assert.IsType<ChordBox>(group.FindName("GlobalActionKey_screenshot")).Chord);

            // A new key for the same action replaces the old one in place.
            group.OnActionChosen("screenshot", "Ctrl+Shift+F11");
            Assert.Equal("Ctrl+Shift+F11", config.GlobalKeys.Actions.Single().Key);

            var remove = Assert.IsType<Button>(group.FindName("GlobalActionRemove_screenshot"));
            remove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Empty(config.GlobalKeys.Actions);
            Assert.Empty(group.GlobalActions.Children);
            Assert.Null(group.FindName("GlobalActionKey_screenshot"));
        });
    }

    /// <summary>
    /// The Info tab lists every key as the person has it, not as the app ships: a moved key shows
    /// where it is now, one taken away is gone, the numbered runs fold into one row each, and a run
    /// whose keys are switched off in Settings is not listed at all.
    /// </summary>
    [Fact]
    public void TheInfoTabListsThePersonsOwnKeys()
    {
        Wpf.Run(() =>
        {
            var info = Wpf.Layout(new Rex.Mirror.Views.InfoPanel(), 320);
            static Dictionary<string, string> Rows(System.Collections.IEnumerable source) =>
                ((IEnumerable<KeyValuePair<string, string>>)source).ToDictionary(row => row.Value, row => row.Key);

            var shipped = Rows(info.ShortcutKeys.ItemsSource);
            Assert.Equal("Ctrl+Alt+H", shipped["Home"]);
            Assert.Equal("Ctrl+Alt+Shift+1 to 9", shipped["Open favourite apps 1 to 9"]);
            Assert.Equal("Ctrl+Alt+F1 to F9", shipped["Apply profiles 1 to 9"]);
            Assert.DoesNotContain("Open favourite app 1", shipped.Keys);

            var config = new RexConfig();
            config.Keys.Window =
            [
                new KeyBinding { Action = "home", Key = "Ctrl+Alt+J" },
                new KeyBinding { Action = "recents", Key = string.Empty },
            ];
            config.Apps.FavouriteKeys = false;
            config.Normalize();
            info.ShowKeys(config);
            var own = Rows(info.ShortcutKeys.ItemsSource);
            Assert.Equal("Ctrl+Alt+J", own["Home"]);
            Assert.DoesNotContain("Recent apps", own.Keys);
            Assert.DoesNotContain(own.Keys, k => k.StartsWith("Open favourite app", StringComparison.Ordinal));
            Assert.Equal("Ctrl+Alt+F1 to F9", own["Apply profiles 1 to 9"]);

            config.Profiles.Keys = false;
            info.ShowKeys(config);
            Assert.DoesNotContain(Rows(info.ShortcutKeys.ItemsSource).Keys, k => k.StartsWith("Apply profile", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TheInfoTabListsThePersonsKeysFromAnywhere()
    {
        Wpf.Run(() =>
        {
            var info = Wpf.Layout(new Rex.Mirror.Views.InfoPanel(), 320);
            static string[] Keys(Rex.Mirror.Views.InfoPanel panel) =>
                [.. ((IEnumerable<KeyValuePair<string, string>>)panel.ShortcutGlobal.ItemsSource).Select(row => row.Key)];
            var config = new RexConfig();
            config.GlobalKeys.Actions = [new() { Key = "Ctrl+Shift+F10", Action = "screenshot" }];

            info.ShowKeys(config);
            Assert.Equal(["Ctrl+Alt+M", "Ctrl+Shift+F10"], Keys(info));
            Assert.Equal("These work while the window is hidden or behind others.", info.ShortcutGlobalNote.Text);
            // The window's own list does not repeat them.
            Assert.DoesNotContain(((IEnumerable<KeyValuePair<string, string>>)info.ShortcutKeys.ItemsSource), row => row.Key == "Ctrl+Alt+M");

            config.GlobalKeys.ShowHide = string.Empty;
            config.GlobalKeys.Actions = [];
            info.ShowKeys(config);
            Assert.Empty(Keys(info));
            Assert.StartsWith("None set.", info.ShortcutGlobalNote.Text, StringComparison.Ordinal);

            config.GlobalKeys.Enabled = false;
            info.ShowKeys(config);
            Assert.StartsWith("Off.", info.ShortcutGlobalNote.Text, StringComparison.Ordinal);
        });
    }
}
