using System;
using System.Globalization;
using System.Windows.Data;

namespace TsuruLauncher.Converters
{
    public class CategoryNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string category = value as string ?? "mod";
            return category switch
            {
                "mod" => "🧩 Minecraft Mod",
                "modpack" => "📦 整合包 (Modpacks)",
                "resourcepack" => "🖼️ 资源包 (Resource Packs)",
                "shader" => "☀️ 光影包 (Shaders)",
                "datapack" => "🏷️ 数据包 (Data Packs)",
                _ => "🧩 资源"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}