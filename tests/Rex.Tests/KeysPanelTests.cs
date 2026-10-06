using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            foreach (var box in boxes.Where(b => b.Text.Length > 0))
            {
                var text = new FormattedText(box.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch), box.FontSize, Brushes.White, 1);
                Assert.True(text.Width + box.Padding.Left + box.Padding.Right + box.BorderThickness.Left + box.BorderThickness.Right <= box.ActualWidth,
                    $"{box.Text} needs {text.Width:0} of the {box.ActualWidth:0} its box has.");
            }

            // Names wrap rather than being cut short, so two that start alike can be told apart.
            var names = boxes.Select(b => ((Grid)b.Parent).Children.OfType<TextBlock>().First()).ToArray();
            Assert.All(names, name => Assert.Equal(TextTrimming.None, name.TextTrimming));
            Assert.All(names, name => Assert.Equal(TextWrapping.Wrap, name.TextWrapping));
            Assert.Equal(KeyMap.WindowIds.Count + KeyMap.BrowseIds.Count, names.Length);
        });
    }
}
