using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using Wpf.Ui.Common;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Controls
{
    /// <summary>
    /// Builds the right click menus used on lists across the app, so every list gets the same look:
    /// an icon, the action, an optional key hint, and red text for anything that removes something.
    /// </summary>
    internal sealed class ItemMenu
    {
        public System.Windows.Controls.ContextMenu Menu { get; }

        private readonly ItemsControl _items;

        public ItemMenu() : this(new System.Windows.Controls.ContextMenu()) { }

        private ItemMenu(System.Windows.Controls.ContextMenu menu)
        {
            Menu = menu;
            _items = menu;
        }

        private ItemMenu(MenuItem parent)
        {
            Menu = new System.Windows.Controls.ContextMenu();
            _items = parent;
        }

        public int Count => _items.Items.Count;

        public ItemMenu Add(string header, SymbolRegular icon, Action action, string gesture = "", bool enabled = true, bool danger = false, bool bold = false)
        {
            var item = new MenuItem
            {
                Header = header,
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = icon },
                InputGestureText = gesture,
                IsEnabled = enabled,
            };

            if (bold)
                item.FontWeight = FontWeights.SemiBold;

            if (danger)
                item.SetResourceReference(Control.ForegroundProperty, "SystemFillColorCriticalBrush");

            item.Click += (_, _) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("ItemMenu", ex);
                }
            };

            _items.Items.Add(item);
            return this;
        }

        public ItemMenu Sub(string header, SymbolRegular icon, Action<ItemMenu> fill, bool enabled = true)
        {
            var parent = new MenuItem
            {
                Header = header,
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = icon },
                IsEnabled = enabled,
            };

            fill(new ItemMenu(parent));

            if (parent.Items.Count == 0)
                parent.IsEnabled = false;

            _items.Items.Add(parent);
            return this;
        }

        public ItemMenu Copy(string header, string? text, string gesture = "")
        {
            return Add(header, SymbolRegular.Copy24, () => ClipboardShare.CopyText(text ?? ""), gesture, enabled: !string.IsNullOrEmpty(text));
        }

        public ItemMenu Link(string header, string url)
        {
            return Add(header, SymbolRegular.Open24, () => Utilities.ShellExecute(url));
        }

        public ItemMenu Separator()
        {
            if (_items.Items.Count > 0 && _items.Items[^1] is not System.Windows.Controls.Separator)
                _items.Items.Add(new System.Windows.Controls.Separator());

            return this;
        }

        /// <summary>Opens the menu at the mouse, next to <paramref name="target"/>.</summary>
        public void Open(UIElement target)
        {
            while (Menu.Items.Count > 0 && Menu.Items[^1] is System.Windows.Controls.Separator)
                Menu.Items.RemoveAt(Menu.Items.Count - 1);

            if (Menu.Items.Count == 0)
                return;

            Menu.PlacementTarget = target;
            Menu.Placement = PlacementMode.MousePoint;
            Menu.IsOpen = true;
        }

        /// <summary>The data item of type <typeparamref name="T"/> behind whatever was clicked, if any.</summary>
        public static T? ItemAt<T>(object? source) where T : class
        {
            DependencyObject? current = source as DependencyObject;

            while (current is not null)
            {
                if (current is FrameworkElement { DataContext: T found })
                    return found;

                if (current is FrameworkContentElement { DataContext: T foundContent })
                    return foundContent;

                current = current is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        /// <summary>
        /// Hooks a list so a right click on one of its rows calls <paramref name="build"/> with that row's data
        /// and opens the menu it fills.
        /// </summary>
        public static void Attach<T>(UIElement list, Action<T, ItemMenu> build) where T : class
        {
            list.PreviewMouseRightButtonUp += (_, e) =>
            {
                T? item = ItemAt<T>(e.OriginalSource);
                if (item is null)
                    return;

                if (ListBoxItemAt(e.OriginalSource) is ListBoxItem row && !row.IsSelected)
                    row.IsSelected = true;

                var menu = new ItemMenu();
                build(item, menu);
                menu.Open(list);
                e.Handled = true;
            };
        }

        private static ListBoxItem? ListBoxItemAt(object? source)
        {
            DependencyObject? current = source as DependencyObject;
            while (current is not null && current is not ListBoxItem)
                current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);

            return current as ListBoxItem;
        }
    }
}
