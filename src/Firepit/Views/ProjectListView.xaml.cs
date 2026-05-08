using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using Firepit.Core.Projects;

namespace Firepit.Views;

public partial class ProjectListView : UserControl
{
    public ProjectListView()
    {
        InitializeComponent();
        Projects = new ObservableCollection<Project>();
        ProjectsList.ItemsSource = Projects;
    }

    public ObservableCollection<Project> Projects { get; }

    public event EventHandler<Project>? ProjectActivated;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectsList.SelectedItem is Project project)
        {
            ProjectActivated?.Invoke(this, project);
        }
    }
}
