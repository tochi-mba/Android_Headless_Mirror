using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

internal interface ISettingsGroup
{
    Expander Group { get; }
    void Attach(SettingsPanel panel, MainWindow window, AppHost host);
    void Refresh(RexConfig config);
}
