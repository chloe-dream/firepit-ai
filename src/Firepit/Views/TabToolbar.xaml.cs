using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Firepit.Core.ProjectConfig;
using Firepit.Core.QuickLinks;
using Firepit.Resources;

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
    public event EventHandler<ProjectCommand>? CommandClicked;

    public void SetCommands(IReadOnlyList<ProjectCommand> commands)
    {
        var buttons = new List<Button>(commands.Count);
        var style = (Style)FindResource("ToolbarIconButton");
        foreach (var cmd in commands)
        {
            if (cmd.Disabled == true) continue;
            var button = new Button
            {
                Content = BuildCommandContent(cmd),
                Style = style,
                Tag = cmd,
                ToolTip = BuildCommandTooltip(cmd),
            };
            button.Click += OnCommandClick;
            buttons.Add(button);
        }
        Commands.ItemsSource = buttons;
    }

    private void OnCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProjectCommand cmd })
        {
            CommandClicked?.Invoke(this, cmd);
        }
    }

    private static string BuildCommandTooltip(ProjectCommand cmd) => cmd.Type switch
    {
        ProjectCommandType.Shell         => $"Shell: {cmd.Command} {string.Join(' ', cmd.Args ?? [])}",
        ProjectCommandType.ClaudePrompt  => $"Send to Claude: \"{Truncate(cmd.Prompt, 80)}\"",
        ProjectCommandType.Url           => $"Open: {cmd.Url}",
        _                                => cmd.Name,
    };

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" :
        s.Length <= max ? s : s[..max] + "…";

    private static StackPanel BuildCommandContent(ProjectCommand cmd)
    {
        var (geometry, mode) = IconResolver.Resolve(cmd.Icon, fallbackName: cmd.Name);

        var path = new Path
        {
            Data = geometry,
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
        };
        var foregroundBinding = new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) },
        };
        if (mode == Firepit.Resources.IconMode.Fill)
        {
            path.SetBinding(Shape.FillProperty, foregroundBinding);
        }
        else
        {
            path.SetBinding(Shape.StrokeProperty, foregroundBinding);
            path.StrokeThickness = 1.2;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.Stretch = Stretch.None;
        }

        var text = new TextBlock
        {
            Text = cmd.Name,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(path);
        panel.Children.Add(text);
        return panel;
    }

    public void SetQuickLinks(IReadOnlyList<ResolvedQuickLink> links)
    {
        var buttons = new List<Button>(links.Count);
        var style = (Style)FindResource("ToolbarIconButton");
        foreach (var link in links)
        {
            var button = new Button
            {
                Content = BuildQuickLinkContent(link),
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

    private static StackPanel BuildQuickLinkContent(ResolvedQuickLink link)
    {
        var (geometry, mode) = ResolveIcon(link.Icon, link.Name);

        var path = new Path
        {
            Data = geometry,
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
        };

        var foregroundBinding = new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) },
        };

        if (mode == IconMode.Fill)
        {
            path.SetBinding(Shape.FillProperty, foregroundBinding);
        }
        else
        {
            path.SetBinding(Shape.StrokeProperty, foregroundBinding);
            path.StrokeThickness = 1.2;
            path.StrokeLineJoin = PenLineJoin.Round;
            // Stroked glyphs are designed at native size; uniform-stretching them
            // would scale the stroke too. Keep them at natural geometry coords.
            path.Stretch = Stretch.None;
        }

        var text = new TextBlock
        {
            Text = link.Name,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(path);
        panel.Children.Add(text);
        return panel;
    }

    private enum IconMode { Stroke, Fill }

    private static (Geometry Geometry, IconMode Mode) ResolveIcon(string? iconHint, string linkName)
    {
        // Explicit Icon field wins; otherwise sniff the link name. Old configs
        // without an Icon get sensible defaults for the seeded entries.
        var key = (iconHint ?? linkName).Trim().ToLowerInvariant();
        return key switch
        {
            "github"    => (FindGeometry("IconGitHub"),    IconMode.Fill),
            "fishbowl"  => (FindGeometry("IconFishbowl"),  IconMode.Fill),
            _           => (FindGeometry("IconLink"),      IconMode.Stroke),
        };
    }

    private static Geometry FindGeometry(string key) => (Geometry)Application.Current.FindResource(key);

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
