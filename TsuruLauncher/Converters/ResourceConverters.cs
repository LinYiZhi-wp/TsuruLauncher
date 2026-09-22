using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using TsuruLauncher.Models.Ecosystem;

namespace TsuruLauncher.Converters
{
    public class EmptyCollectionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable enumerable)
            {
                var count = enumerable.Cast<object>().Count();
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class SelectedVersionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? fileName = value as string;
            if (DataContextHelper.CurrentResource?.SelectedVersion?.FileName == fileName)
                return "#25FFD700";
            return "#12FFFFFF";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class SelectedVersionBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? fileName = value as string;
            if (DataContextHelper.CurrentResource?.SelectedVersion?.FileName == fileName)
                return "#50FFD700";
            return "#18FFFFFF";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class ProgressBarWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is double progress && values[1] is double width)
            {
                return width * Math.Max(0, Math.Min(1, progress));
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public static class DataContextHelper
    {
        public static ResourceDetail? CurrentResource { get; set; }
    }
}