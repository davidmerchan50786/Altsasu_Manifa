#if VISTA
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Serializes graph elements into lightweight JSON payloads that also carry the concrete runtime type needed for reconstruction.
    /// </summary>
    public static class Serializer
    {
        /// <summary>
        /// Temporarily assigns <see cref="target"/> so serialization warnings can be logged against a specific Unity object.
        /// </summary>
        public struct TargetScope : IDisposable
        {
            /// <summary>
            /// Sets the temporary logging target used by this serializer.
            /// </summary>
            /// <param name="target">Unity object to associate with serialization warnings during the scope lifetime.</param>
            public TargetScope(UnityEngine.Object target)
            {
                Serializer.target = target;
            }

            /// <summary>
            /// Clears the temporary logging target when the scope ends.
            /// </summary>
            public void Dispose()
            {
                Serializer.target = null;
            }
        }

        [Serializable]
        /// <summary>
        /// Stores the minimal type information needed to locate a serialized object's runtime type again.
        /// </summary>
        public struct TypeInfo
        {
            [SerializeField]
            /// <summary>
            /// Full type name used to search through loaded assemblies during deserialization.
            /// </summary>
            public string fullName;

            /// <summary>
            /// Indicates whether this record contains enough data to attempt a type lookup.
            /// </summary>
            public bool IsValid
            {
                get
                {
                    return !string.IsNullOrEmpty(fullName);
                }
            }
        }

        [Serializable]
        /// <summary>
        /// Combines serialized JSON data with the concrete type information required to reconstruct the original object.
        /// </summary>
        public struct JsonObject : IEquatable<JsonObject>
        {
            [SerializeField]
            /// <summary>
            /// Serialized runtime type descriptor.
            /// </summary>
            public TypeInfo typeInfo;

            [SerializeField]
            /// <summary>
            /// Raw JSON payload produced by <see cref="JsonUtility.ToJson(object)"/>.
            /// </summary>
            public string jsonData;

            /// <summary>
            /// Compares both the serialized type name and the JSON payload.
            /// </summary>
            /// <param name="other">Serialized object to compare against.</param>
            public bool Equals(JsonObject other)
            {
                return String.Compare(typeInfo.fullName, other.typeInfo.fullName) == 0 &&
                        String.Compare(jsonData, other.jsonData) == 0;
            }

            /// <summary>
            /// Serializes this wrapper itself into JSON for debugging or nested storage.
            /// </summary>
            public override string ToString()
            {
                return JsonUtility.ToJson(this);
            }
        }

        /// <summary>
        /// Returns a sentinel serialized value representing an empty element.
        /// </summary>
        public static JsonObject NullElement
        {
            get
            {
                return new JsonObject()
                {
                    typeInfo = new TypeInfo(),
                    jsonData = null
                };
            }
        }

        /// <summary>
        /// Unity object associated with warning logs emitted by the batch serialize and deserialize helpers.
        /// </summary>
        public static UnityEngine.Object target { get; set; }

        /// <summary>
        /// Converts a runtime type into the compact type record stored inside <see cref="JsonObject"/>.
        /// </summary>
        /// <param name="type">Runtime type to record.</param>
        public static TypeInfo GetTypeAsSerializedData(Type type)
        {
            return new TypeInfo
            {
                fullName = type.FullName
            };
        }

        /// <summary>
        /// Resolves a runtime type by searching the currently loaded assemblies for the stored full type name.
        /// </summary>
        /// <param name="typeInfo">Serialized type descriptor.</param>
        /// <returns>The matching runtime type, or <see langword="null"/> if no loaded assembly defines it.</returns>
        public static Type GetTypeFromSerializedData(TypeInfo typeInfo)
        {
            if (!typeInfo.IsValid)
                return null;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                Type type = assembly.GetType(typeInfo.fullName);
                if (type != null)
                    return type;
            }

            return null;
        }

        /// <summary>
        /// Serializes one object into a <see cref="JsonObject"/> that preserves both its JSON payload and concrete type.
        /// </summary>
        /// <typeparam name="T">Compile-time type of the item being serialized.</typeparam>
        /// <param name="item">Object to serialize.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is <see langword="null"/>.</exception>
        public static JsonObject Serialize<T>(T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item", $"Cannot serialize null item");
            }

            TypeInfo typeInfo = GetTypeAsSerializedData(item.GetType());
            string jsonData = JsonUtility.ToJson(item);

            JsonObject serializedElement = new JsonObject()
            {
                typeInfo = typeInfo,
                jsonData = jsonData
            };
            return serializedElement;
        }

        /// <summary>
        /// Reconstructs one object from its serialized wrapper and optional constructor arguments.
        /// </summary>
        /// <typeparam name="T">Expected base type of the deserialized object.</typeparam>
        /// <param name="item">Serialized wrapper containing the runtime type and JSON payload.</param>
        /// <param name="constructorArgs">Constructor arguments used when creating the instance before overwriting fields from JSON.</param>
        /// <exception cref="ArgumentException">Thrown when the serialized type is invalid or cannot be resolved from loaded assemblies.</exception>
        public static T Deserialize<T>(JsonObject item, params object[] constructorArgs) where T : class
        {
            if (!item.typeInfo.IsValid)
            {
                throw new ArgumentException($"Cannot deserialize the item, object type is invalid.");
            }

            Type type = GetTypeFromSerializedData(item.typeInfo);
            if (type == null)
            {
                throw new ArgumentException($"Cannot deserialize the item, the type [{item.typeInfo.fullName}] is not exist.");
            }

            T instance;
            try
            {
                CultureInfo culture = CultureInfo.CurrentCulture;
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                instance = Activator.CreateInstance(type, flags, null, constructorArgs, culture) as T;
            }
            catch
            {
                throw;
            }

            if (instance != null && !string.IsNullOrEmpty(item.jsonData))
            {
                JsonUtility.FromJsonOverwrite(item.jsonData, instance);
            }
            return instance;
        }

        /// <summary>
        /// Serializes a sequence into a list of <see cref="JsonObject"/> wrappers, skipping failed items and logging warnings instead of aborting the whole batch.
        /// </summary>
        /// <typeparam name="T">Compile-time type of the items being serialized.</typeparam>
        /// <param name="items">Sequence to serialize.</param>
        public static List<JsonObject> Serialize<T>(IEnumerable<T> items)
        {
            List<JsonObject> serializedItems = new List<JsonObject>();
            if (items == null)
            {
                return serializedItems;
            }

            foreach (T i in items)
            {
                try
                {
                    serializedItems.Add(Serialize(i));
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e, target);
                }
            }
            return serializedItems;
        }

        /// <summary>
        /// Deserializes a sequence of serialized wrappers, skipping failed items and logging warnings instead of aborting the whole batch.
        /// </summary>
        /// <typeparam name="T">Expected base type of the deserialized objects.</typeparam>
        /// <param name="items">Serialized wrappers to reconstruct.</param>
        /// <param name="constructorArgs">Constructor arguments forwarded to each object creation call.</param>
        public static List<T> Deserialize<T>(IEnumerable<JsonObject> items, params object[] constructorArgs) where T : class
        {
            List<T> deserializedItems = new List<T>();
            if (items == null)
            {
                return deserializedItems;
            }

            foreach (JsonObject i in items)
            {
                try
                {
                    T item = Deserialize<T>(i, constructorArgs);
                    deserializedItems.Add(item);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e, target);
                }
            }
            return deserializedItems;
        }
    }
}
#endif


