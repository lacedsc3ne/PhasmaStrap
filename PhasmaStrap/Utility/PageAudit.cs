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
