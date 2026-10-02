using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.Elements.Settings.Search;

using SettingsModel = PhasmaStrap.Models.Persistable.Settings;

namespace PhasmaStrap.UI.Elements.Settings
{
    /// <summary>
    /// The right click menu shared by every setting row (<see cref="OptionControl"/>).
    /// Hooked once for the whole app as a class handler from OptionControl's static constructor.
    /// </summary>
    internal static class SettingRowMenu
    {
        private const string LOG_IDENT = "SettingRowMenu";

        private static readonly Lazy<SettingsModel> Defaults = new(() => new SettingsModel());

        public static void OnRowRightClick(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || sender is not OptionControl row)
                return;

            if (e.OriginalSource is not DependencyObject source || !ShouldOpen(source, row))
                return;

            var menu = new ItemMenu();
            Build(row, menu);
            menu.Open(row);
            e.Handled = true;
        }

        private static void Build(OptionControl row, ItemMenu menu)
        {
            string header = (row.Header ?? "").Trim();
            string description = PlainText(row.Description);
            string helpLink = row.HelpLink ?? "";

            ResetTarget? reset = null;
            try
            {
                reset = FindResetTarget(row);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            menu.Add("Reset to default", SymbolRegular.ArrowReset24, () => reset?.Apply(), enabled: reset is not null && !reset.IsDefault, bold: true)
                .Separator();

            MainWindow? window = Window.GetWindow(row) as MainWindow;
            menu.Add("Find related settings", SymbolRegular.Search24, () =>
            {
                if (window is null)
                    return;

                // Run after the menu closes so it doesn't take focus back from the search box.
                MainWindow target = window;
                target.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => target.SearchFor(header)));
            }, enabled: window is not null && header.Length > 0);

            menu.Add("What does this do?", SymbolRegular.Info24, () =>
            {
                string message = header.Length > 0 ? $"{header}\n\n{description}" : description;
                Frontend.ShowMessageBox(message, MessageBoxImage.Information);
            }, enabled: description.Length > 0);

            if (helpLink.Length > 0)
                menu.Add("Open help page", SymbolRegular.Link24, () => Utilities.ShellExecute(helpLink));

            SettingsSearchEntry? entry = null;
            try
            {
                entry = SettingLink.FindEntry(header, PageTypesAround(row));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            menu.Separator()
                .Copy("Copy setting name", header)
                .Add("Copy link to this setting", SymbolRegular.Link24, () =>
                {
                    if (entry is not null)
                        ClipboardShare.CopyText(SettingLink.WebLink(entry));
                }, enabled: entry is not null);
        }

        /// <summary>The settings pages this row sits in, innermost first (a section page, then the page hosting it).</summary>
        private static List<Type> PageTypesAround(OptionControl row)
        {
            string? pagesNamespace = typeof(Pages.SettingsPage).Namespace;
            var types = new List<Type>();

            for (DependencyObject? current = row; current is not null; current = ParentOf(current))
            {
                Type type = current.GetType();
                if (type.Namespace == pagesNamespace && !types.Contains(type))
                    types.Add(type);
            }

            return types;
        }

        #region Where the click landed

        private static DependencyObject? ParentOf(DependencyObject current)
        {
            DependencyObject? parent = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : null;

            return parent ?? LogicalTreeHelper.GetParent(current);
        }

        private static bool ShouldOpen(DependencyObject source, OptionControl row)
        {
            // Between the click and the row: leave text boxes and anything with its own menu alone.
            for (DependencyObject? current = source; current is not null && !ReferenceEquals(current, row); current = ParentOf(current))
            {
                if (current is TextBoxBase or PasswordBox)
                    return false;

                if (current is FrameworkElement { ContextMenu: not null } or FrameworkContentElement { ContextMenu: not null })
                    return false;
            }

            if (row.ContextMenu is not null)
                return false;

            // Rows that belong to a list item keep the list's own menu.
            for (DependencyObject? current = row; current is not null; current = ParentOf(current))
            {
                if (current is ListBoxItem or DataGridRow or TreeViewItem)
                    return false;

                if (ItemsControl.ItemsControlFromItemContainer(current) is ItemsControl owner)
                {
                    object item = owner.ItemContainerGenerator.ItemFromContainer(current);
                    if (item != DependencyProperty.UnsetValue && !ReferenceEquals(item, current))
                        return false;
                }
            }

            return true;
        }

        #endregion

        #region Reset to default

        private sealed class ResetTarget
        {
            public object Source { get; init; } = null!;
            public PropertyInfo SourceProperty { get; init; } = null!;
            public PropertyInfo SettingProperty { get; init; } = null!;
            public object? Default { get; init; }
            public List<BindingExpression> Expressions { get; } = new();

            public bool IsDefault => Equals(SettingProperty.GetValue(App.Settings.Prop), Default);

            public void Apply()
            {
                // Set through the bound property so the view model runs its own logic and notifications.
                SourceProperty.SetValue(Source, Default);

                foreach (BindingExpression expression in Expressions)
                    expression.UpdateTarget();

                if (!IsDefault)
                    App.Logger.WriteLine(LOG_IDENT, $"Reset of {SettingProperty.Name} did not stick");
            }
        }

        /// <summary>
        /// The one setting this row edits, or null when the row edits nothing, edits more than one thing,
        /// or edits something that can't be traced back to a property on the settings file.
        /// </summary>
        private static ResetTarget? FindResetTarget(OptionControl row)
        {
            ResetTarget? found = null;

            foreach (DependencyObject element in Descendants(row))
            {
                LocalValueEnumerator values = element.GetLocalValueEnumerator();
                while (values.MoveNext())
                {
                    DependencyProperty property = values.Current.Property;
                    BindingExpressionBase? expressionBase = BindingOperations.GetBindingExpressionBase(element, property);
                    if (expressionBase is null || !IsTwoWay(expressionBase, property, element))
                        continue;

                    if (expressionBase is not BindingExpression expression)
                        return null;

                    object? source = expression.ResolvedSource;
                    string? name = expression.ResolvedSourcePropertyName;

                    // Bindings inside a control's own template point at other UI elements, not at settings.
                    if (source is null || source is DependencyObject)
                        continue;

                    if (string.IsNullOrEmpty(name))
                        return null;

                    if (found is not null)
                    {
                        if (ReferenceEquals(found.Source, source) && found.SourceProperty.Name == name)
                        {
                            found.Expressions.Add(expression);
                            continue;
                        }

                        return null;
                    }

                    found = Map(source, name);
                    if (found is null)
                        return null;

                    found.Expressions.Add(expression);
                }
            }

            return found;
        }

        private static ResetTarget? Map(object source, string name)
        {
            PropertyInfo? setting = PublicProperty(typeof(SettingsModel), name);
            if (setting is null || !IsSimple(setting.PropertyType))
                return null;

            PropertyInfo? bound = ReferenceEquals(source, App.Settings.Prop) ? setting : PublicProperty(source.GetType(), name);
            if (bound is null || bound.PropertyType != setting.PropertyType)
                return null;

            // Same name and type isn't enough on its own: the bound property must also show the saved value.
            if (!Equals(bound.GetValue(source), setting.GetValue(App.Settings.Prop)))
                return null;

            return new ResetTarget
            {
                Source = source,
                SourceProperty = bound,
                SettingProperty = setting,
                Default = setting.GetValue(Defaults.Value),
            };
        }

        private static PropertyInfo? PublicProperty(Type type, string name)
        {
            PropertyInfo? property;
            try
            {
                property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            }
            catch (AmbiguousMatchException)
            {
                return null;
            }

            if (property is null || property.GetIndexParameters().Length > 0)
                return null;

            if (property.GetGetMethod() is null || property.GetSetMethod() is null)
                return null;

            return property;
        }

        private static bool IsSimple(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal);
        }

        private static bool IsTwoWay(BindingExpressionBase expression, DependencyProperty property, DependencyObject element)
        {
            BindingMode mode = expression.ParentBindingBase switch
            {
                Binding binding => binding.Mode,
                MultiBinding multi => multi.Mode,
                _ => BindingMode.Default,
            };

            if (mode == BindingMode.TwoWay || mode == BindingMode.OneWayToSource)
                return true;

            return mode == BindingMode.Default
                && property.GetMetadata(element.GetType()) is FrameworkPropertyMetadata { BindsTwoWayByDefault: true };
        }

        private static IEnumerable<DependencyObject> Descendants(OptionControl row)
        {
            var pending = new Stack<DependencyObject>();
            pending.Push(row);

            while (pending.Count > 0)
            {
                DependencyObject current = pending.Pop();

                // A row inside a row has its own menu.
                if (current is OptionControl && !ReferenceEquals(current, row))
                    continue;

                yield return current;

                if (current is not Visual and not System.Windows.Media.Media3D.Visual3D)
                    continue;

                int count = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < count; i++)
                    pending.Push(VisualTreeHelper.GetChild(current, i));
            }
        }

        #endregion

        private static string PlainText(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return "";

            string text = Regex.Replace(markdown, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            text = Regex.Replace(text, @"(\*\*|__|==|`)", "");
            text = Regex.Replace(text, @"(?<!\w)[*_](?=\S)|(?<=\S)[*_](?!\w)", "");
            return text.Trim();
        }
    }
}
