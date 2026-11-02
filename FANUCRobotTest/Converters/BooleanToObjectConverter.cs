using System;
using System.Globalization;
using System.Windows.Data;

namespace FANUCRobotTest.Converters
{
    public class BooleanToObjectConverter : IValueConverter
    {
        public object TrueValue { get; init; }
        public object FalseValue { get; init; }
        public object NullValue { get; init; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? TrueValue : FalseValue;
            return NullValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (Equals(value, TrueValue))
                return true;
            if (Equals(value, FalseValue))
                return false;
            return null;
        }
    }
}
