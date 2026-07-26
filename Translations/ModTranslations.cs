using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Mods;
using Mafi.Localization;

namespace Iserik.FaFOptimiser.Translations
{
    public static class ModTranslations
    {
        private const string TRANSLATIONS_DIR = "Translations";
        private static bool s_loaded;

        public static bool Load(ModManifest manifest)
        {
            if (s_loaded) return true;
            s_loaded = true;

            string modRoot = manifest?.RootDirectoryPath;
            try
            {
                LocalizationManager.LangInfo lang = LocalizationManager.CurrentLangInfo;

                // Skip if English or if mod root is unknown
                if (lang.CultureInfoId == "en-US" || string.IsNullOrEmpty(modRoot)) return true;

                string filePath = Path.Combine(modRoot, TRANSLATIONS_DIR, lang.FileName);
                if (!File.Exists(filePath))
                {
                    Log.Info($"[FaFOptimiser] No translation file for '{lang.CultureInfoId}' at '{filePath}'.");
                    return true;
                }

                string json = File.ReadAllText(filePath);
                if (!LocalizationUtils.TryParseJsonFileData(json, out Dict<string, LocalizationManager.LocData> parsed, out string error))
                {
                    Log.Warning($"[FaFOptimiser] Failed to parse '{filePath}': {error}");
                    return true;
                }

                FieldInfo sDataField = typeof(LocalizationManager).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic);
                if (sDataField == null)
                {
                    Log.Warning("[FaFOptimiser] LocalizationManager.s_data field not found via reflection.");
                    return true;
                }

                if (!(sDataField.GetValue(null) is Dict<string, LocalizationManager.LocData> sData))
                {
                    sData = new Dict<string, LocalizationManager.LocData>();
                    sDataField.SetValue(null, sData);
                }

                int count = 0;
                foreach (KeyValuePair<string, LocalizationManager.LocData> kvp in parsed)
                {
                    sData[kvp.Key] = kvp.Value;
                    count++;
                }

                Log.Info($"[FaFOptimiser] Loaded {count} translations for '{lang.CultureInfoId}' from '{filePath}'.");

                LocalizationManager.ScanForStaticLocStrFields(typeof(ModTranslations).Assembly);
                RebindStaticLocStrs(typeof(ModTranslations).Assembly, sData);
            }
            catch (Exception)
            {
                Log.Warning("[FaFOptimiser] Exception Failed to load mod translations.");
            }
            return true;
        }

        private static void RebindStaticLocStrs(Assembly asm, Dict<string, LocalizationManager.LocData> sData)
        {
            Type locDataType = typeof(LocalizationManager.LocData);
            FieldInfo locDataArrayField = null;

            foreach (FieldInfo f2 in locDataType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Type ft = f2.FieldType;
                // FIX: Changed !ft.IsGenericType to ft.IsGenericType
                if (ft.IsGenericType && ft.Name.StartsWith("ImmutableArray", StringComparison.Ordinal))
                {
                    Type[] args = ft.GetGenericArguments();
                    if (args.Length == 1 && args[0] == typeof(string))
                    {
                        locDataArrayField = f2;
                        break;
                    }
                }
            }

            if (locDataArrayField == null)
            {
                Log.Warning("[FaFOptimiser] RebindStaticLocStrs: could not find ImmutableArray<string> field on LocData.");
                return;
            }

            Type immutableArrayType = locDataArrayField.FieldType;
            PropertyInfo lengthProp = immutableArrayType.GetProperty("Length", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo itemProp = immutableArrayType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(p => p.GetIndexParameters().Length == 1 && p.GetIndexParameters()[0].ParameterType == typeof(int) && p.PropertyType == typeof(string));

            if (lengthProp == null || itemProp == null)
            {
                Log.Warning("[FaFOptimiser] RebindStaticLocStrs: ImmutableArray<string> shape lacks expected Length/indexer.");
                return;
            }

            Dictionary<Type, LocStrShape> shapesByType = new Dictionary<Type, LocStrShape>();
            foreach (Type t in typeof(LocStr).Assembly.GetTypes().Where(t => t.Namespace == "Mafi.Localization"))
            {
                FieldInfo[] stringFields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(f => f.FieldType == typeof(string)).ToArray();
                FieldInfo idField = stringFields.FirstOrDefault(f => f.Name == "Id");

                if (idField != null)
                {
                    FieldInfo[] translationSlots = stringFields.Where(f => f != idField).ToArray();
                    if (translationSlots.Length > 0)
                    {
                        shapesByType[t] = new LocStrShape(idField, translationSlots);
                    }
                }
            }

            int rebound = 0, missing = 0;
            object[] indexBuf = new object[1];

            foreach (Type type in asm.GetTypes())
            {
                FieldInfo[] fields;
                try { fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                catch { continue; }

                foreach (FieldInfo field in fields)
                {
                    if (shapesByType.TryGetValue(field.FieldType, out LocStrShape shape))
                    {
                        try
                        {
                            object boxed = field.GetValue(null);
                            if (boxed != null)
                            {
                                string id = shape.IdField.GetValue(boxed) as string;
                                if (!string.IsNullOrEmpty(id))
                                {
                                    if (!sData.TryGetValue(id, out LocalizationManager.LocData data))
                                    {
                                        missing++;
                                    }
                                    else
                                    {
                                        object array = locDataArrayField.GetValue(data);
                                        if (array != null && (int)lengthProp.GetValue(array) > 0)
                                        {
                                            bool wrote = false;
                                            int slotsToFill = System.Math.Min((int)lengthProp.GetValue(array), shape.Slots.Length);
                                            for (int i = 0; i < slotsToFill; i++)
                                            {
                                                indexBuf[0] = i;
                                                string translation = itemProp.GetValue(array, indexBuf) as string;
                                                if (!string.IsNullOrEmpty(translation))
                                                {
                                                    shape.Slots[i].SetValue(boxed, translation);
                                                    wrote = true;
                                                }
                                            }
                                            if (wrote)
                                            {
                                                field.SetValue(null, boxed);
                                                rebound++;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Exception(ex, $"[FaFOptimiser] Rebind failed for {type.FullName}.{field.Name}");
                        }
                    }
                }
            }
            Log.Info($"[FaFOptimiser] Rebound {rebound} static LocStr fields ({missing} keys not in custom json).");
        }

        private readonly struct LocStrShape
        {
            public readonly FieldInfo IdField;
            public readonly FieldInfo[] Slots;

            public LocStrShape(FieldInfo id, FieldInfo[] slots)
            {
                IdField = id;
                Slots = slots;
            }
        }
    }
}