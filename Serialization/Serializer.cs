using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Serialization
{
    public class Serializer
    {
        public bool Compressing = true;
        private readonly ICompressor compressor;

        public readonly Dictionary<Type, (Func<object, byte[]>, Func<byte[], object>)> Converters = new();

        public static int NestingLevel { get; set; } = 8;


        public Serializer() : this(new SZipCompressor())
        {
        }

        public Serializer(ICompressor compressor)
        {
            this.compressor = compressor;
            Converters[typeof(string)] =
                (x => Encoding.UTF8.GetBytes((string)x), bytes => Encoding.UTF8.GetString(bytes));
        }

        public void AddCustom(Serializer customSerializer)
        {
            foreach (var (type, convs) in customSerializer.Converters)
            {
                Converters[type] = convs;
            }
        }

        public void AddConverters(Type t, (Func<object, byte[]>, Func<byte[], object>) converters) =>
            this.Converters[t] = converters;

        public byte[] Serialize<T>(T obj)
        {
            var buffer = GetSerializeData(obj).ToList();
            buffer.AddRange(MD5.HashData(buffer.ToArray()));
            if (Compressing)
                return compressor.Compress(buffer.ToArray());
            return buffer.ToArray();
        }

        private byte[] GetSerializeData<T>(T obj, int nestingLevel = 0)
        {
            if (obj == null) return BitConverter.GetBytes(0);
            if (nestingLevel > 8)
                throw new ArgumentException("cyclical ref or too much nesting level");
            if (Converters.ContainsKey(obj.GetType()))
            {
                var data = Converters[obj.GetType()].Item1(obj);
                var dummy = new List<byte>();
                dummy.AddRange(BitConverter.GetBytes(data.Length));
                dummy.AddRange(data);
                return dummy.ToArray();
            }

            var result = SerializePrimitiveObject(obj);
            if (result is { Length: <= 4 }) result = SerializeCollection(obj as ICollection);

            if (result is { Length: > 4 })
                return result;
            var buffer = SerializeObject(obj, nestingLevel + 1);

            return buffer.ToArray();
        }

        private byte[] SerializeObject<T>(T obj, int nestingLevel)
        {
            var buffer = new List<byte>();
            buffer.AddRange(new byte[] { 0, 0, 0, 0 });
            var type = obj.GetType();
            foreach (var propertyInfo in type.GetRuntimeFields().OrderBy(x => x.Name).ToArray())
                buffer.AddRange(GetSerializeData(propertyInfo.GetValue(obj), nestingLevel));
            var result = buffer.ToArray();
            BitConverter.GetBytes(result.Length - 4).CopyTo(result, 0);
            return result;
        }

        private byte[] SerializeCollection(ICollection collection)
        {
            if (collection == null) return BitConverter.GetBytes(0);
            var data = new List<byte>() { 0, 0, 0, 0 };
            foreach (var item in collection)
                data.AddRange(GetSerializeData(item));
            var length = BitConverter.GetBytes(data.Count - 4);
            var result = data.ToArray();
            length.CopyTo(result, 0);
            return result;
        }

        private byte[] SerializePrimitiveObject<T>(T obj)
        {
            if (!obj.GetType().IsPrimitive)
                return BitConverter.GetBytes(0);
            var result = new List<byte>();
            byte[] data;
            if (Converters.TryGetValue(obj.GetType(), out var convs))
                data = convs.Item1(obj);
            else
            {
                var size = Marshal.SizeOf(obj);
                data = new byte[size];
                var ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(obj, ptr, false);
                Marshal.Copy(ptr, data, 0, size);
            }

            result.AddRange(BitConverter.GetBytes(data.Length));
            result.AddRange(data);

            return result.ToArray();
        }

        public T Deserialize<T>(byte[] bytes)
        {
            var data = Compressing ? compressor.Decompress(bytes) : bytes;
            var hash = data[^16..];
            data = data[..^16];
            if (MD5.HashData(data).Equals(hash))
                throw new ArgumentException("bad hash");
            return CreateObject<T>(data);
        }


        private T CreateObject<T>(byte[] serialized)
        {
            T instance = default;
            var size = BitConverter.ToInt32(serialized[..4]);
            if (size == 0)
                return default;

            if (TryGetPrimitive(serialized, out instance))
                return instance;

            if (Converters.ContainsKey(typeof(T)))
                return (T)Converters[typeof(T)].Item2(serialized[4..]);

            if (typeof(T).IsAssignableTo(typeof(Array)))
                return CreateArray<T>(serialized);

            instance = MakeObject<T>(serialized);

            return instance;
        }

        private T MakeObject<T>(byte[] serialized)
        {
            T instance;
            instance = Activator.CreateInstance<T>();
            var t = typeof(T);
            var items = GetItemsBytes(serialized);
            var fields = t.GetRuntimeFields().OrderBy(x => x.Name).ToArray();
            var flags = BindingFlags.NonPublic;
            flags += (int)BindingFlags.Instance;
            for (var i = 0; i < items.Count; i++)
            {
                var valueBytes = items[i];

                var method = this.GetType().GetMethod("CreateObject", flags)
                    .MakeGenericMethod(fields[i].FieldType);
                var value = method
                    .Invoke(this, new object[] { valueBytes });
                fields[i].SetValue(instance, value);
            }

            return instance;
        }

        private T CreateArray<T>(byte[] serialized)
        {
            var itemsBytes = GetItemsBytes(serialized);
            var array = (Array)Activator.CreateInstance(typeof(T), args: itemsBytes.Count);
            var flags = BindingFlags.NonPublic;
            flags += (int)BindingFlags.Instance;
            for (var i = 0; i < itemsBytes.Count; i++)
            {
                var item = itemsBytes[i];
                var method = this.GetType().GetMethod("CreateObject", flags)
                    .MakeGenericMethod(typeof(T).GetElementType());

                var value = method
                    .Invoke(this, new object[] { item });

                array.SetValue(value, i);
            }

            object result = array;
            return (T)result;
        }

        private List<byte[]> GetItemsBytes(byte[] serialized)
        {
            var result = new List<byte[]>();
            BitConverter.ToInt32(serialized[..4]);
            serialized = serialized[4..];
            while (serialized.Length > 0)
            {
                var length = BitConverter.ToInt32(serialized[..4]);
                var item = serialized[..(length + 4)];
                result.Add(item);
                serialized = serialized[(length + 4)..];
            }

            return result;
        }

        private static bool TryGetPrimitive<T>(byte[] serialized, out T obj)
        {
            if (!typeof(T).IsPrimitive)
            {
                obj = default;
                return false;
            }

            var result = Activator.CreateInstance<T>();
            var size = BitConverter.ToInt32(serialized[..4]);
            serialized = serialized[4..];
            var ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(serialized, 0, ptr, size);
            obj = Marshal.PtrToStructure<T>(ptr);
            return true;
        }
    }
}