using MWL4.Models;
using MWL4.ViewModels;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MWL4
{
    /// <summary>
    /// Shows the complete dataset returned for one worklist item as a tree, including sequences and private tags.
    /// </summary>
    public partial class DatasetDetailsWindow : Window
    {
        private readonly List<DatasetNode> _nodes;

        public DatasetDetailsWindow(WorklistItem item)
        {
            InitializeComponent();

            var title = "Dataset details";
            if (!string.IsNullOrEmpty(item?.PatientName)) title += " - " + item.PatientName;
            if (!string.IsNullOrEmpty(item?.AccessionNumber)) title += " (" + item.AccessionNumber + ")";
            Title = title;

            _nodes = DatasetNode.Build(item?.Dataset);
            if (_nodes.Count == 0)
            {
                _nodes.Add(new DatasetNode { Text = "No dataset available." });
            }
            DatasetTree.ItemsSource = _nodes;
        }

        private void SetExpanded(ItemsControl parent, bool expanded)
        {
            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
                {
                    tvi.IsExpanded = expanded;
                    if (expanded) tvi.UpdateLayout(); // generate child containers
                    SetExpanded(tvi, expanded);
                }
            }
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e) => SetExpanded(DatasetTree, true);

        private void CollapseAllButton_Click(object sender, RoutedEventArgs e) => SetExpanded(DatasetTree, false);

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(DatasetNode.ToText(_nodes));
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                MessageBox.Show(this, "Could not copy to the clipboard:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
