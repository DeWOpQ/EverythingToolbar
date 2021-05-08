using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
#if SYNAPTICS
using System.Runtime.InteropServices;
using SYNCTRLLib;
#endif

namespace EverythingToolbar
{
    public partial class SearchResultsView
    {
        private SearchResult SelectedItem => SearchResultsListView.SelectedItem as SearchResult;
        private Point dragStart;
#if SYNAPTICS
        static SynAPICtrl api;
        static SynDeviceCtrl synTouchPad;
        static SynPacketCtrl synPacket;
        static int deviceHandle;
        private int cumulativeYDelta = 0;
#endif

        public SearchResultsView()
        {
            InitializeComponent();

            SearchResultsListView.ItemsSource = EverythingSearch.Instance.SearchResults;
            EverythingSearch.Instance.ResultsCleared += OnResultsCleared;
            ((INotifyCollectionChanged)SearchResultsListView.Items).CollectionChanged += OnCollectionChanged;

#if SYNAPTICS
            try
            {
                api = new SynAPICtrl();
                synTouchPad = new SynDeviceCtrl();
                synPacket = new SynPacketCtrl();

                api.Initialize();
                api.Activate();
                deviceHandle = api.FindDevice(SynConnectionType.SE_ConnectionAny, SynDeviceType.SE_DeviceTouchPad, -1);
                synTouchPad.Select(deviceHandle);
                synTouchPad.Activate();
                synTouchPad.OnPacket += OnSynapticsPacket;
            }
            catch (COMException) { }
#endif
        }

#if SYNAPTICS
        private void OnSynapticsPacket()
        {
            synTouchPad.LoadPacket(synPacket);
            var numberOfFingers = synPacket.GetLongProperty(SynPacketProperty.SP_ExtraFingerState);
            numberOfFingers &= 0b11;

            if (numberOfFingers == 2)
            {
                cumulativeYDelta += synPacket.YDelta;
                if (Math.Abs(cumulativeYDelta) > 100)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        Decorator listViewBorder = VisualTreeHelper.GetChild(SearchResultsListView, 0) as Decorator;
                        ScrollViewer listViewScrollViewer = listViewBorder.Child as ScrollViewer;
                        double vOffset = cumulativeYDelta < 0 ? -3 : 3;
                        listViewScrollViewer.ScrollToVerticalOffset(listViewScrollViewer.VerticalOffset + vOffset);
                    }), DispatcherPriority.ContextIdle);
                    cumulativeYDelta = 0;
                }
            }
            else
            {
                cumulativeYDelta = 0;
            }
        }
#endif

        private void OnResultsCleared(object sender, EventArgs e)
        {
            ScrollToVerticalOffset(0);
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (SearchResultsListView.SelectedIndex == -1 && SearchResultsListView.Items.Count > 0)
                SearchResultsListView.SelectedIndex = 0;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange > 0)
            {
                if (e.VerticalOffset > e.ExtentHeight - 2 * e.ViewportHeight)
                {
                    EverythingSearch.Instance.QueryBatch();
                    ScrollToVerticalOffset(e.VerticalOffset);
                }
            }
        }

        public void ScrollToVerticalOffset(double verticalOffset)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                Decorator listViewBorder = VisualTreeHelper.GetChild(SearchResultsListView, 0) as Decorator;
                ScrollViewer listViewScrollViewer = listViewBorder.Child as ScrollViewer;
                listViewScrollViewer.ScrollToVerticalOffset(verticalOffset);
            }), DispatcherPriority.ContextIdle);
        }

        public void SelectNextSearchResult()
        {
            if (SearchResultsListView.SelectedIndex + 1 < SearchResultsListView.Items.Count)
            {
                SearchResultsListView.SelectedIndex++;
                SearchResultsListView.ScrollIntoView(SearchResultsListView.SelectedItem);
            }
        }

        public void SelectPreviousSearchResult()
        {
            if (SearchResultsListView.SelectedIndex > 0)
            {
                SearchResultsListView.SelectedIndex--;
                SearchResultsListView.ScrollIntoView(SelectedItem);
            }
        }

        public void OpenSelectedSearchResult()
        {
            if (SearchResultsListView.SelectedIndex == -1)
                SelectNextSearchResult();

            if (SearchResultsListView.SelectedIndex != -1)
            {
                if (Rules.HandleRule(SelectedItem))
                    return;

                SelectedItem?.Open();
            }
        }

        public void OpenFilePath(object sender, RoutedEventArgs e)
        {
            SelectedItem?.OpenPath();
        }

        public void PreviewSelectedFile()
        {
            SelectedItem?.PreviewInQuickLook();
        }

        private void CopyPathToClipBoard(object sender, RoutedEventArgs e)
        {
            SelectedItem?.CopyPathToClipboard();
            EverythingSearch.Instance.Reset();
        }

        private void OpenWith(object sender, RoutedEventArgs e)
        {
            SelectedItem?.OpenWith();
        }

        private void ShowInEverything(object sender, RoutedEventArgs e)
        {
            SelectedItem?.ShowInEverything();
        }

        private void CopyFile(object sender, RoutedEventArgs e)
        {
            SelectedItem?.CopyToClipboard();
            EverythingSearch.Instance.Reset();
        }

        private void Open(object sender, RoutedEventArgs e)
        {
            OpenSelectedSearchResult();
        }

        private void Open(object sender, MouseEventArgs e)
        {
            switch (Keyboard.Modifiers)
            {
                case ModifierKeys.Alt:
                    SelectedItem?.ShowProperties();
                    break;
                case ModifierKeys.Control:
                    SelectedItem?.OpenPath();
                    break;
                case ModifierKeys.Shift:
                    SelectedItem?.ShowInEverything();
                    break;
                default:
                    OpenSelectedSearchResult();
                    break;
            }
        }

        public void RunAsAdmin(object sender, RoutedEventArgs e)
        {
            SelectedItem?.RunAsAdmin();
        }

        public void ShowFileProperties(object sender, RoutedEventArgs e)
        {
            SelectedItem?.ShowProperties();
        }

        private void OnOpenWithMenuLoaded(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;

            while (mi.Items.Count > 3)
                mi.Items.RemoveAt(0);

            List<Rule> rules = Rules.LoadRules();

            if (rules.Count > 0)
                (mi.Items[0] as MenuItem).Visibility = Visibility.Collapsed;
            else
                (mi.Items[0] as MenuItem).Visibility = Visibility.Visible;

            for (int i = rules.Count - 1; i >= 0; i--)
            {
                Rule rule = rules[i];
                MenuItem ruleMenuItem = new MenuItem() { Header = rule.Name, Tag = rule.Command };
                ruleMenuItem.Click += OpenWithRule;
                mi.Items.Insert(0, ruleMenuItem);
            }
        }

        private void OpenWithRule(object sender, RoutedEventArgs e)
        {
            SearchResult searchResult = SearchResultsListView.SelectedItem as SearchResult;
            string command = (sender as MenuItem).Tag?.ToString() ?? "";
            Rules.HandleRule(searchResult, command);
        }

        private void OnListViewItemClicked(object sender, MouseButtonEventArgs e)
        {
            dragStart = PointToScreen(Mouse.GetPosition(this));

            var item = (sender as Border).DataContext;
            SearchResultsListView.SelectedIndex = SearchResultsListView.Items.IndexOf(item);
        }

        private void OnListViewItemMouseMove(object sender, MouseEventArgs e)
        {
            Vector diff = dragStart - PointToScreen(Mouse.GetPosition(this));

            if (e.LeftButton == MouseButtonState.Pressed &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                if (SearchResultsListView.SelectedItems.Count == 0)
                    return;

                string[] files = { SelectedItem?.FullPathAndFileName };
                var data = new DataObject(DataFormats.FileDrop, files);
                data.SetData(DataFormats.Text, files[0]);
                DragDrop.DoDragDrop(SearchResultsListView, data, DragDropEffects.All);
            }
        }

        private void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            ContextMenu cm = sender as ContextMenu;
            MenuItem mi = cm.Items[2] as MenuItem;

            string[] extensions = { ".exe", ".bat", ".cmd" };
            bool isExecutable = (bool)SelectedItem?.IsFile && extensions.Any(ext => SelectedItem.FullPathAndFileName.EndsWith(ext));

            if (isExecutable)
                mi.Visibility = Visibility.Visible;
            else
                mi.Visibility = Visibility.Collapsed;
        }
    }
}
