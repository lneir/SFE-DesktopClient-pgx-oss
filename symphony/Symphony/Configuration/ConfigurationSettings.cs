﻿using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Symphony.Configuration
{
    public class ConfigurationSettings : IConfigurationSettings
    {
        private readonly string path;
        private JObject cachedSettingsDocument;

        public ConfigurationSettings(string path)
        {
            this.path = path;
        }

        public TEnum ConvertEnumValueOrDefault<TEnum>(string @namespace, string key, TEnum defaultValue) where TEnum : struct
        {
            lock (this)
            {
                var root = this.GetRoot(@namespace);

                if (root != null)
                {
                    JToken token;

                    if (root.TryGetValue(key, out token))
                    {
                        if (typeof(TEnum).IsEnum)
                        {
                            try
                            {
                                return (TEnum)Enum.ToObject(
                                    typeof(TEnum),
                                    token.ToObject<int>());
                            }
                            catch (Exception)
                            {
                                return defaultValue;
                            }
                        }

                        throw new NotSupportedException();
                    }
                }

                return defaultValue;
            }
        }

        public T GetValueOrDefault<T>(string @namespace, string key, T defaultValue)
        {
            lock (this)
            {
                var root = this.GetRoot(@namespace);

                if (root != null)
                {
                    JToken token;

                    if (root.TryGetValue(key, out token))
                    {
                        if (typeof(T).IsEnum)
                        {
                            try
                            {
                                T value = (T)Enum.Parse(typeof(T), token.ToString(), true);
                                return value;
                            }
                            catch (Exception)
                            {
                                return defaultValue;
                            }
                        }

                        return token.ToObject<T>();
                    }
                }

                return defaultValue;
            }
        }

        public TEnum GetEnumValueOrDefault<TEnum>(string @namespace, string key, TEnum defaultValue) 
            where TEnum : struct
        {
            lock (this)
            {
                var root = this.GetRoot(@namespace);

                if (root != null)
                {
                    JToken token;

                    if (root.TryGetValue(key, out token))
                    {
                        if (typeof(TEnum).IsEnum)
                        {
                            try
                            {
                                return (TEnum)Enum.Parse(typeof(TEnum), token.ToString(), true);
                            }
                            catch
                            {
                                return defaultValue;
                            }
                        }

                        throw new NotSupportedException();
                    }
                }

                return defaultValue;
            }
        }

        public void Load()
        {
            try
            {
                using (var file = File.Open(this.path, FileMode.OpenOrCreate))
                using (var reader = new StreamReader(file, Encoding.UTF8))
                {
                    try
                    {
                        var document = JObject.Parse(reader.ReadToEnd());
                        this.cachedSettingsDocument = document;
                    }
                    catch (Exception)
                    {
                        this.cachedSettingsDocument = new JObject();
                    }
                }
            }
            catch (Exception)
            {
                this.cachedSettingsDocument = new JObject();
            }
        }

        public void Write(string @namespace, JObject settings)
        {
            lock (this)
            {
                this.cachedSettingsDocument[@namespace] = settings;
                
                this.Save(this.cachedSettingsDocument);
            }
        }

        private void Save(JObject document)
        {
            using (var file = File.Open(this.path, FileMode.Create, FileAccess.ReadWrite))
            using (var writer = new StreamWriter(file, Encoding.UTF8))
            {
                writer.Write(document.ToString());
            }
        }

        private JObject GetRoot(string @namespace)
        {
            return this.cachedSettingsDocument[@namespace] as JObject;
        }
    }
}
