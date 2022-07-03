using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace ObjectReading
{
    public class ObjectReader
    {
        public T CreateObject<T>(string serialized)
        {
            
            T instance = default;
            if (TryGetSimple(serialized, out T o)) return o;

            var lines = serialized.Split('\n');

            if (typeof(T).IsAssignableTo(typeof(Array)))
            {
                List<string[]> itemsLines = new List<string[]>();
                lines = lines[1..];
                while (lines.Length>0)
                {
                    var valueLines = GetNextPropertyValueLines(lines);
                    lines = lines[valueLines.Length..];
                    itemsLines.Add(valueLines);
                }
                var array = (Array) Activator.CreateInstance(typeof(T), args: itemsLines.Count);

                for (var i = 0; i < itemsLines.Count; i++)
                {
                    var itemLines = itemsLines[i];
                    
                    var method = this.GetType().GetMethod("CreateObject")
                        .MakeGenericMethod(typeof(T).GetElementType());
                    var value = method
                        .Invoke(this, new object[] {string.Join('\n', itemLines)});
                    array.SetValue(value,i);
                }

                return instance;
            }

            instance = Activator.CreateInstance<T>();
            var t = typeof(T);

            if (!serialized.StartsWith(typeof(T).Name))
                throw new ArgumentException("invalid serialized data");

            lines = lines[1..];
            while (lines.Length > 0)
            {
                var propertyInfo = t.GetProperty(lines[0].Split('=')[0].Trim());
                var valueLines = GetNextPropertyValueLines(lines);
                var method = this.GetType().GetMethod("CreateObject")
                    .MakeGenericMethod(propertyInfo.PropertyType);
                var value = method
                    .Invoke(this, new object[] {string.Join('\n', valueLines)});
                propertyInfo.SetValue(instance, value);
                lines = lines[valueLines.Length..];
            }

            return instance;
        }

        private static bool TryGetSimple<T>(string serialized, out T o)
        {
            if (serialized.Contains('=') && serialized.Split('=')[1].Trim() == "null")
            {
                o = default;
                return true;
            }

            T instance = default;
            var flag = true;
            try
            {
                instance = (T) TypeDescriptor.GetConverter(typeof(T)).ConvertFromString(serialized);
            }
            catch (Exception e)
            {
                flag = false;
            }

            o = flag ? instance : default;

            return flag;
        }

        private string[] GetNextPropertyValueLines(string[] lines)
        {
            var valueIndex = lines[0].IndexOf('=') + 1;
            if (valueIndex == -1)
                return lines;
            lines[0] = lines[0][valueIndex..];
            if (lines.Length <= 1)
                return lines;

            var result = new List<string>() {lines[0]};
            var tabsCount = lines[1].Split('=')[0].Count(x => x == '\t');
            lines = lines.Skip(1)
                .TakeWhile(x => x.StartsWith(new string('\t', tabsCount)))
                .Select(x => x[(tabsCount - 1)..])
                .ToArray();

            return result.Concat(lines).ToArray();
        }
    }
}