using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using UnityEngine;

namespace jKnepel.SimpleUnityNetworking.Serialisation
{
    public class Reader
    {
        #region fields

        public int Position;
        public int Length => _buffer.Length;
        public int Remaining => Length - Position;
        
        private readonly byte[] _buffer;

        private static readonly ConcurrentDictionary<Type, Func<Reader, object>> _typeHandlerCache = new();
        private static readonly HashSet<Type> _unknownTypes = new();

        #endregion

        #region lifecycle

        public Reader(byte[] bytes) : this(new ArraySegment<byte>(bytes)) { }

        public Reader(ArraySegment<byte> bytes)
		{
            if (bytes.Array == null)
                return;

            Position = bytes.Offset;
            _buffer = bytes.Array;
		}

        static Reader()
        {   // caches all implemented type handlers during compilation
            CreateTypeHandlerDelegate(typeof(bool));
            CreateTypeHandlerDelegate(typeof(byte));
            CreateTypeHandlerDelegate(typeof(sbyte));
            CreateTypeHandlerDelegate(typeof(ushort));
            CreateTypeHandlerDelegate(typeof(short));
            CreateTypeHandlerDelegate(typeof(uint));
            CreateTypeHandlerDelegate(typeof(int));
            CreateTypeHandlerDelegate(typeof(ulong));
            CreateTypeHandlerDelegate(typeof(long));
            CreateTypeHandlerDelegate(typeof(string));
            CreateTypeHandlerDelegate(typeof(char));
            CreateTypeHandlerDelegate(typeof(float));
            CreateTypeHandlerDelegate(typeof(double));
            CreateTypeHandlerDelegate(typeof(decimal));
            CreateTypeHandlerDelegate(typeof(Vector2));
            CreateTypeHandlerDelegate(typeof(Vector3));
            CreateTypeHandlerDelegate(typeof(Vector4));
            CreateTypeHandlerDelegate(typeof(Matrix4x4));
            CreateTypeHandlerDelegate(typeof(Color));
            CreateTypeHandlerDelegate(typeof(Color32));
            CreateTypeHandlerDelegate(typeof(DateTime));
        }

        internal static void Init() { }

		#endregion

		#region automatic type handler

		public T Read<T>()
		{
            Type type = typeof(T);
            return (T)Read(type);
        }

        private object Read(Type type)
		{
            if (!_unknownTypes.Contains(type))
			{
                if (_typeHandlerCache.TryGetValue(type, out Func<Reader, object> handler))
                {   // check for already cached type handler delegates
                    return handler(this);
                }

                Func<Reader, object> customHandler = CreateTypeHandlerDelegate(type, true);
                if (customHandler != null)
                {   // use custom type handler if user defined method was found
                    return customHandler(this);
                }

                // TODO : remove this once pre-compile cached generic handlers are supported
                Func<Reader, object> implementedHandler = CreateTypeHandlerDelegate(type, false);
                if (implementedHandler != null)
                {   // use implemented type handler
                    return implementedHandler(this);
                }

                // save types that don't have any a type handler and need to be recursively serialised
                _unknownTypes.Add(type);
            }

            // recursively serialise type if no handler is found
            // TODO : circular dependencies will cause crash
            // TODO : add attributes for serialisation
            // TODO : add serialisation options to handle size, circular dependencies etc. 
            // TODO : handle properties
            FieldInfo[] fieldInfos = type.GetFields();
            if (fieldInfos.Length == 0 || fieldInfos.Where(x => x.FieldType == type).Any())
            {   // TODO : circular dependencies will cause crash
                string typeName = GetTypeName(type);
                throw new SerialiseNotImplemented($"No read method implemented for the type {typeName}!"
                    + $" Implement a Read{typeName} method or use an extension method in the parent type!");
			}

            object obj = FormatterServices.GetUninitializedObject(type);
            foreach (FieldInfo fieldInfo in fieldInfos)
                fieldInfo.SetValue(obj, Read(fieldInfo.FieldType));
            return obj;
        }

        private static string GetTypeName(Type type)
        {
            if (type.IsArray)
                return "Array";

            if (!type.IsGenericType)
                return type.Name;

            int index = type.Name.IndexOf("`");
            return type.Name.Substring(0, index);
        }

        /// <summary>
        /// Constructs and caches pre-compiled expression delegate of type handlers.
        /// </summary>
        /// <remarks>
        /// TODO : also cache generic handlers during compilation
        /// </remarks>
        /// <param name="type">The type of the variable for which the writer is defined</param>
        /// <param name="useCustomReader">Wether the reader method is an instance of the Reader class or a custom static method in the type</param>
        /// <returns></returns>
        private static Func<Reader, object> CreateTypeHandlerDelegate(Type type, bool useCustomReader = false)
        {   // find implemented or custom read method
            var readerMethod = useCustomReader
                ?           type.GetMethod($"Read{GetTypeName(type)}", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                : typeof(Reader).GetMethod($"Read{GetTypeName(type)}", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (readerMethod == null)
                return null;

            // parameters
            var instanceArg = Expression.Parameter(typeof(Reader), "instance");

            // construct handler call body
            MethodCallExpression call;
            if (readerMethod.IsGenericMethod)
            {
                var genericReader = type.IsArray
                    ? readerMethod.MakeGenericMethod(type.GetElementType())
                    : readerMethod.MakeGenericMethod(type.GetGenericArguments());
                call = useCustomReader
                    ? Expression.Call(genericReader, instanceArg)
                    : Expression.Call(instanceArg, genericReader);
            }
            else
            {
                call = useCustomReader
                    ? Expression.Call(readerMethod, instanceArg)
                    : Expression.Call(instanceArg, readerMethod);
            }

            // cache delegate
            var castResult = Expression.Convert(call, typeof(object));
            var lambda = Expression.Lambda<Func<Reader, object>>(castResult, instanceArg);
            var action = lambda.Compile();
            _typeHandlerCache.TryAdd(type, action);
            return action;
        }

        #endregion

        #region helpers

        public void Skip(int val)
		{
            if (val < 1 || val > Remaining)
                return;

            Position += val;
		}

        public void Clear()
		{
            Position += Remaining;
		}

        public byte[] ReadBuffer()
		{
            return _buffer;
		}

        public void BlockCopy(ref byte[] dst, int dstOffset, int count)
		{
            if (count > Remaining)
                throw new IndexOutOfRangeException("The BlockCopy count exceeds the remaining length!");

            Buffer.BlockCopy(_buffer, Position, dst, dstOffset, count);
            Position += count;
		}

        public byte[] ReadRemainingBytes()
		{
            byte[] remaining = new byte[Remaining];
            BlockCopy(ref remaining, 0, Remaining);
            return remaining;
		}

        public ArraySegment<byte> ReadByteSegment(int count)
		{
            if (count > Remaining)
                throw new IndexOutOfRangeException("The ReadByteSegment count exceeds the remaining length!");

            ArraySegment<byte> result = new(_buffer, Position, count);
            Position += count;
            return result;
        }

		#endregion

		#region primitives

        public bool ReadBoolean()
		{
            byte result = _buffer[Position++];
            return result == 1;
        }

        public byte ReadByte()
		{
            byte result = _buffer[Position++];
            return result;
		}

        public sbyte ReadSByte()
		{
            sbyte result = (sbyte)_buffer[Position++];
            return result;
		}

        public ushort ReadUInt16()
		{
            ushort result = _buffer[Position++];
            result |= (ushort)(_buffer[Position++] << 8);
            return result;
		}

        public short ReadInt16()
		{
            return (short)ReadUInt16();
		}

        public uint ReadUInt32()
		{
            uint result = _buffer[Position++];
            result |= (uint)(_buffer[Position++] << 8);
            result |= (uint)(_buffer[Position++] << 16);
            result |= (uint)(_buffer[Position++] << 24);
            return result;
		}

        public int ReadInt32()
		{
            return (int)ReadUInt32();
		}

        public ulong ReadUInt64()
		{
            ulong result = _buffer[Position++];
            result |= (ulong)_buffer[Position++] << 8;
            result |= (ulong)_buffer[Position++] << 16;
            result |= (ulong)_buffer[Position++] << 24;
            result |= (ulong)_buffer[Position++] << 32;
            result |= (ulong)_buffer[Position++] << 40;
            result |= (ulong)_buffer[Position++] << 48;
            result |= (ulong)_buffer[Position++] << 56;
            return result;
        }

        public long ReadInt64()
		{
            return (long)ReadUInt64();
		}

        public char ReadChar()
		{
            char result = (char)_buffer[Position++];
            result |= (char)(_buffer[Position++] << 8);
            return result;
        }

        public float ReadSingle()
		{
            TypeConverter.UIntToFloat converter = new() { UInt = ReadUInt32() };
            return converter.Float;
        }

        public double ReadDouble()
		{
            TypeConverter.ULongToDouble converter = new() { ULong = ReadUInt64() };
            return converter.Double;
        }

        public decimal ReadDecimal()
		{
            TypeConverter.ULongsToDecimal converter = new() { ULong1 = ReadUInt64(), ULong2 = ReadUInt64() };
            return converter.Decimal;
        }

		#endregion

		#region unity objects

        public Vector2 ReadVector2()
		{
            return new Vector2(ReadSingle(), ReadSingle());
		}

        public Vector3 ReadVector3()
		{
            return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
		}

        public Vector4 ReadVector4()
		{
            return new Vector4(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
		}

        public Quaternion ReadQuaternion()
		{
            return new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
		}

        public Matrix4x4 ReadMatrix4x4()
		{
            Matrix4x4 result = new()
			{
                m00 = ReadSingle(), m01 = ReadSingle(), m02 = ReadSingle(), m03 = ReadSingle(),
                m10 = ReadSingle(), m11 = ReadSingle(), m12 = ReadSingle(), m13 = ReadSingle(),
                m20 = ReadSingle(), m21 = ReadSingle(), m22 = ReadSingle(), m23 = ReadSingle(),
                m30 = ReadSingle(), m31 = ReadSingle(), m32 = ReadSingle(), m33 = ReadSingle()
            };
            return result;
        }

        public Color ReadColor()
		{
            float r = (float)(ReadByte() / 100.0f);
            float g = (float)(ReadByte() / 100.0f);
            float b = (float)(ReadByte() / 100.0f);
            float a = (float)(ReadByte() / 100.0f);
            return new Color(r, g, b, a);
		}

        public Color ReadColorWithoutAlpha()
		{
            float r = (float)(ReadByte() / 100.0f);
            float g = (float)(ReadByte() / 100.0f);
            float b = (float)(ReadByte() / 100.0f);
            return new Color(r, g, b, 1);
        }

        public Color32 ReadColor32()
		{
            return new Color32(ReadByte(), ReadByte(), ReadByte(), ReadByte());
		}

        public Color32 ReadColor32WithoutAlpha()
		{
            return new Color32(ReadByte(), ReadByte(), ReadByte(), 255);
        }

		#endregion

		#region objects

        public string ReadString()
		{
            ushort length = ReadUInt16();
            ArraySegment<byte> bytes = ReadByteSegment(length);
            return Encoding.ASCII.GetString(bytes);
		}

        public string ReadStringWithoutFlag(int length)
        {
            ArraySegment<byte> bytes = ReadByteSegment(length);
            return Encoding.ASCII.GetString(bytes);
        }

        public T[] ReadArray<T>()
		{
            int length = ReadInt32();
            T[] array = new T[length];
            for (int i = 0; i < length; i++)
                array[i] = Read<T>();
            return array;
		}

        public List<T> ReadList<T>()
		{
            int count = ReadInt32();
            List<T> list = new(count);
            for (int i = 0; i < count; i++)
                list.Add(Read<T>());
            return list;
        }

        public Dictionary<TKey, TValue> ReadDictionary<TKey, TValue>()
		{
            int count = ReadInt32();
            Dictionary<TKey, TValue> dictionary = new(count);
            for (int i = 0; i < count; i++)
                dictionary.Add(Read<TKey>(), Read<TValue>());
            return dictionary;
        }

        public DateTime ReadDateTime()
		{
            return DateTime.FromBinary(ReadInt64());
		}

        #endregion
    }
}
