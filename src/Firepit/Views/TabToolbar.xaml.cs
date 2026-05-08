using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Firepit.Core.QuickLinks;

namespace Firepit.Views;

public partial class TabToolbar : UserControl
{
    public TabToolbar()
    {
        InitializeComponent();
    }

    public event EventHandler? RekindleRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? ExplorerRequested;
    public event EventHandler? ShellRequested;
    public event EventHandler<ResolvedQuickLink>? QuickLinkClicked;

    public void SetQuickLinks(IReadOnlyList<ResolvedQuickLink> links)
    {
        var buttons = new List<Button>(links.Count);
        var style = (Style)FindResource("ToolbarButton");
        foreach (var link in links)
        {
            var button = new Button
            {
                Content = link.Name,
                Style = style,
                Tag = link,
                IsEnabled = link.Available,
                ToolTip = link.Available
                    ? link.Url
                    : $"{link.Name} — {link.UnavailableReason}",
            };
            button.Click += OnQuickLinkClick;
            buttons.Add(button);
        }
        QuickLinks.ItemsSource = buttons;
    }

    private void OnRekindleClick(object sender, RoutedEventArgs e) => RekindleRequested?.Invoke(this, EventArgs.Empty);
    private void OnResumeClick(object sender, RoutedEventArgs e)   => ResumeRequested?.Invoke(this, EventArgs.Empty);
    private void OnExplorerClick(object sender, RoutedEventArgs e) => ExplorerRequested?.Invoke(this, EventArgs.Empty);
    private void OnShellClick(object sender, RoutedEventArgs e)    => ShellRequested?.Invoke(this, EventArgs.Empty);

    private void OnQuickLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ResolvedQuickLink link })
        {
            QuickLinkClicked?.Invoke(this, link);
        }
    }
}
