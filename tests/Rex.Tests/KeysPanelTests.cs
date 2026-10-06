using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Views.Controls;
using Rex.Mirror.Views.Settings;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The Keyboard shortcuts group without a window, at the side panel's narrowest.</summary>
public sealed class KeysPanelTests
{
    [Fact]
    public void EveryKeyFitsItsBoxAndEveryNameIsReadWhole()
    {
        Wpf.Run(() =>
        {
            var group = new KeysGroup();
            group.Refresh(new RexConfig());
            group.GroupKeys.IsExpanded = true;
            Wpf.Layout(group, 280);

            var boxes = Wpf.Logical(group).OfType<ChordBox>().ToArray();
            Assert.NotEmpty(boxes);
            // Each key measured in a box of its own, styled as the row's is and free to be as wide as
            // it likes: the text, the box's padding and border, and the room its template keeps.
            foreach (var box in boxes.Where(b => b.Text.Length > 0))
            {
                var probe = new ChordBox { Padding = box.Padding, Chord = box.Text };
                var holder = new StackPanel { Orientation = Orientation.Horizontal };
                holder.Children.Add(probe);
                Wpf.Layout(holder, 1000);
                Assert.True(probe.DesiredSize.Width <= box.ActualWidth,
                    $"{box.Text} needs {probe.DesiredSize.Width:0} of the {box.ActualWidth:0} its box has.");
            }

            Assert.All(boxes, box => Assert.Equal(KeysGroup.BoxWidth, box.ActualWidth));

            // Names wrap rather than being cut short, so two that start alike can be told apart.
            var names = boxes.Select(b => ((Grid)b.Parent).Children.OfType<TextBlock>().First()).ToArray();
            Assert.All(names, name => Assert.Equal(TextTrimming.None, name.TextTrimming));
            Assert.All(names, name => Assert.Equal(TextWrapping.Wrap, name.TextWrapping));
            Assert.Equal(KeyMap.WindowIds.Count + KeyMap.BrowseIds.Count, names.Length);
        });
    }
}
