using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace PhasmaStrap.Utility
{
    public static class PageAudit
    {
        private const string LOG_IDENT = "PageAudit";

        public static bool Running { get; private set; }

        private static int _crashed;
        private static string _current = "";

        public static void Crashed(Exception ex)
        {
            _crashed++;

            Exception inner = ex;
            while (inner.InnerException is not null)
                inner = inner.InnerException;

            App.Logger.WriteLine(LOG_IDENT, $"CRASH on {_current}: {inner.GetType().Name}: {inner.Message}");

            string? where = inner.StackTrace?.Split(Environment.NewLine).FirstOrDefault(line => line.Contains("PhasmaStrap"))?.Trim();
            if (where is not null)
                App.Logger.WriteLine(LOG_IDENT, $"  {where}");
        }

        public static void Run(UI.Elements.Settings.MainWindow window)
        {
            List<Type> pages = typeof(UI.Elements.Settings.MainWindow).Assembly.GetTypes()
                .Where(type => type.Namespace == "PhasmaStrap.UI.Elements.Settings.Pages" && !type.IsAbstract && typeof(FrameworkElement).IsAssignableFrom(type))
                .OrderBy(type => type.Name)
                .ToList();

            App.Logger.WriteLine(LOG_IDENT, $"Visiting {pages.Count} page(s)");
            CheckAppStyles();

            int index = -1;
            int broken = 0;
            int unreachable = 0;
            bool reached = false;

            Running = true;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };

            timer.Tick += (_, _) =>
            {
                if (index >= 0 && reached)
                {
                    var found = new HashSet<string>();
                    int checkedCount = 0;

                    Inspect(window, found, new HashSet<DependencyObject>(), ref checkedCount);

                    foreach (string line in found.OrderBy(text => text))
                    {
                        broken++;
                        App.Logger.WriteLine(LOG_IDENT, $"BROKEN on {_current}: {line}");
                    }

                    App.Logger.WriteLine(LOG_IDENT, $"{_current}: {checkedCount} binding(s) checked, {found.Count} broken");
                }

                index++;

                if (index >= pages.Count)
                {
                    timer.Stop();
                    App.Logger.WriteLine(LOG_IDENT, $"Finished: {pages.Count} page(s), {unreachable} not reachable, {broken} broken binding(s), {_crashed} crash(es)");
                    App.SoftTerminate();
                    return;
                }

                _current = pages[index].Name;

                try
                {
                    reached = window.Navigate(pages[index]);

                    if (!reached)
                    {
                        unreachable++;
                        App.Logger.WriteLine(LOG_IDENT, $"UNREACHABLE {_current}: nothing navigates to it");
                    }
                }
                catch (Exception ex)
                {
                    reached = false;
                    Crashed(ex);
                }
            };

            timer.Start();
        }

        private static readonly Type[] StyledTypes =
        {
            typeof(System.Windows.Controls.CheckBox), typeof(System.Windows.Controls.RadioButton), typeof(System.Windows.Controls.Button),
            typeof(System.Windows.Controls.Primitives.ToggleButton), typeof(System.Windows.Controls.ComboBox), typeof(System.Windows.Controls.ComboBoxItem),
            typeof(System.Windows.Controls.TextBox), typeof(System.Windows.Controls.PasswordBox), typeof(System.Windows.Controls.Slider),
            typeof(System.Windows.Controls.ListBox), typeof(System.Windows.Controls.ListBoxItem), typeof(System.Windows.Controls.ProgressBar),
            typeof(System.Windows.Controls.Expander), typeof(System.Windows.Controls.TabControl), typeof(System.Windows.Controls.TabItem),
            typeof(System.Windows.Controls.DataGrid), typeof(System.Windows.Controls.ListView), typeof(System.Windows.Controls.ListViewItem),
            typeof(System.Windows.Controls.ContextMenu), typeof(System.Windows.Controls.ToolTip),
            typeof(System.Windows.Controls.Primitives.ScrollBar), typeof(System.Windows.Controls.ScrollViewer),
        };

        private static readonly Dictionary<Type, System.Windows.Controls.ControlTemplate?> PlainTemplates = new();

        private static System.Windows.Controls.ControlTemplate? PlainTemplateOf(Type type)
        {
            if (PlainTemplates.TryGetValue(type, out System.Windows.Controls.ControlTemplate? known))
                return known;

            System.Windows.Controls.ControlTemplate? template = null;

            try
            {
                if (Activator.CreateInstance(type) is System.Windows.Controls.Control bare)
                {
                    bare.BeginInit();
                    bare.Style = new Style(type);
                    bare.EndInit();
                    bare.ApplyTemplate();
                    template = bare.Template;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read the Windows look of {type.Name}: {ex.Message}");
            }

            PlainTemplates[type] = template;
            return template;
        }

        private static bool LooksLikePlainWindows(System.Windows.Controls.Control control)
        {
            if (Array.IndexOf(StyledTypes, control.GetType()) < 0)
                return false;

            System.Windows.Controls.ControlTemplate? plain = PlainTemplateOf(control.GetType());
            return plain is not null && ReferenceEquals(control.Template, plain);
        }

        private static void CheckAppStyles()
        {
            var cursorOnly = new Style(typeof(System.Windows.Controls.CheckBox));
            cursorOnly.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Arrow));

            var broken = new System.Windows.Controls.CheckBox();
            broken.BeginInit();
            broken.Style = cursorOnly;
            broken.EndInit();
            broken.ApplyTemplate();
            App.Logger.WriteLine(LOG_IDENT, $"A check box styled the broken way is caught: {LooksLikePlainWindows(broken)} (has a template: {broken.Template is not null})");

            foreach (Type type in StyledTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is not System.Windows.Controls.Control sample)
                        continue;

                    sample.BeginInit();
                    sample.Style = Application.Current.TryFindResource(type) as Style;
                    sample.EndInit();
                    sample.ApplyTemplate();

                    if (sample.Template is null)
                        App.Logger.WriteLine(LOG_IDENT, $"Could not check {type.Name}: it has no template");

                    if (LooksLikePlainWindows(sample))
                        App.Logger.WriteLine(LOG_IDENT, $"BROKEN everywhere: {type.Name} is drawn with the plain Windows look");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not check {type.Name}: {ex.Message}");
                }
            }
        }

        private static bool EndsOnNothing(object? item, string path)
        {
            if (item is null)
                return true;

            foreach (string part in path.Split('.'))
            {
                if (part.Length == 0 || part.Contains('[') || part.Contains('('))
                    return false;

                System.Reflection.PropertyInfo? property = item.GetType().GetProperty(part);
                if (property is null)
                    return false;

                item = property.GetValue(item);
                if (item is null)
                    return true;
            }

            return false;
        }

        private static void Inspect(DependencyObject node, HashSet<string> found, HashSet<DependencyObject> seen, ref int checkedCount)
        {
            if (!seen.Add(node))
                return;

            if (node is System.Windows.Controls.Control control && control.IsVisible && LooksLikePlainWindows(control))
            {
                string label = string.IsNullOrEmpty(control.Name) ? "" : $" \"{control.Name}\"";
                found.Add($"{control.GetType().Name}{label} is drawn with the plain Windows look");
            }

            LocalValueEnumerator values = node.GetLocalValueEnumerator();

            while (values.MoveNext())
            {
                if (BindingOperations.GetBindingExpression(node, values.Current.Property) is not BindingExpression expression)
                    continue;

                if (expression.Status is BindingStatus.Unattached or BindingStatus.Inactive or BindingStatus.Detached)
                    continue;

                checkedCount++;

                if (expression.Status is not (BindingStatus.PathError or BindingStatus.UpdateTargetError))
                    continue;

                if (expression.Status == BindingStatus.UpdateTargetError && expression.ParentBinding.FallbackValue != DependencyProperty.UnsetValue)
                    continue;

                string path = expression.ParentBinding.Path?.Path ?? "";

                if (expression.Status == BindingStatus.PathError && EndsOnNothing(expression.DataItem, path))
                    continue;

                string item = expression.DataItem?.GetType().Name ?? "nothing";
                string name = node is FrameworkElement element && !string.IsNullOrEmpty(element.Name) ? $" \"{element.Name}\"" : "";

                found.Add($"{node.GetType().Name}{name}.{values.Current.Property.Name} <- \"{path}\" on {item} ({expression.Status})");
            }

            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                int count = VisualTreeHelper.GetChildrenCount(node);

                for (int i = 0; i < count; i++)
                    Inspect(VisualTreeHelper.GetChild(node, i), found, seen, ref checkedCount);
            }

            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is DependencyObject dependency)
                    Inspect(dependency, found, seen, ref checkedCount);
            }
        }
    }
}
