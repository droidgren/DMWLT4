using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MWL4
{
    /// <summary>
    /// In-app help guide. The topics list is built from the top-level Sections of the document (their Tag is the title);
    /// selecting a topic scrolls it to the top of the viewer.
    /// </summary>
    public partial class HelpWindow : Window
    {
        public class Topic
        {
            public string Title { get; set; }
            public Section Section { get; set; }
            public override string ToString() => Title;
        }

        public List<Topic> Topics { get; }

        public HelpWindow()
        {
            InitializeComponent();

            Topics = HelpDocument.Blocks.OfType<Section>()
                .Where(s => s.Tag is string)
                .Select(s => new Topic { Title = (string)s.Tag, Section = s })
                .ToList();
            TopicList.ItemsSource = Topics;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }

        /// <summary>Scrolls the given topic (by title) to the top and selects it in the list.</summary>
        public void ShowTopic(string title)
        {
            var topic = Topics.FirstOrDefault(t => t.Title == title);
            if (topic != null) TopicList.SelectedItem = topic;
        }

        private void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TopicList.SelectedItem is Topic topic)
            {
                ScrollToSection(topic.Section);
            }
        }

        private void ScrollToSection(Section section)
        {
            var scrollViewer = FindChild<ScrollViewer>(Viewer);
            if (scrollViewer == null)
            {
                section.BringIntoView();
                return;
            }

            // Measure with the document scrolled to the top, so the position is the same whether the
            // character rect is relative to the viewport or to the document. This happens in one layout
            // pass, so the jump to the top is not visible.
            scrollViewer.ScrollToVerticalOffset(0);
            scrollViewer.UpdateLayout();
            var rect = section.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
            {
                section.BringIntoView();
                return;
            }
            // Leave a little room above the heading (and reach the very top for the first topic).
            const double headingRoom = 12;
            scrollViewer.ScrollToVerticalOffset(System.Math.Max(0, rect.Top - headingRoom));
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
