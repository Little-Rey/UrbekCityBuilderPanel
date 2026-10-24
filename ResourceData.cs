using ResourceDebugMod;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

public static class ResourceData
{
    private const int MaxPrerequisiteChainDepth = 16;
    private const int MaxPrerequisiteNodesVisited = 256;

    public enum Category
    {
        Storage,
        Inventory,
        ConstructionStorage,
        ConstructionUnlock,
        ConstructionVariables,
        ResourceRate,
        ResourceRateX,
        GrossResourceRate,
        GlobalVariables,
        ConstructionData
    }

    public class Entry
    {
        public object Key;
        public object Value;
        public object Dictionary;
        public string KeyName;
        public string SourceName;
        public string CategoryName;
        public Type ValueType;
        public Action<object> Setter;
        public bool ReapplyAfterUpdate;
    }

    public class ConstructionInfo
    {
        public string Name;
        public string CodeName;
        public string Description;
        public List<ConstructionDetail> Details = new List<ConstructionDetail>();
    }

    public class ConstructionChoice
    {
        public string Name;
        public string CodeName;
    }

    public class ConstructionDetail
    {
        public string Name;
        public object Value;
    }

    private sealed class ObjectReferenceComparer : IEqualityComparer<object>
    {
        public new bool Equals(object left, object right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }

    private class DictionarySource
    {
        public object Dictionary;
        public string Name;
    }

    private class GlobalVariableOverride
    {
        public object Dictionary;
        public object Key;
        public object Value;
    }

    private static readonly List<GlobalVariableOverride> GlobalVariableOverrides = new List<GlobalVariableOverride>();

    public static List<Entry> GetEntries(
        Motor motor,
        Category category,
        out bool hasSources,
        Control control = null,
        string constructionCode = null)
    {
        if (category == Category.ConstructionData)
        {
            List<Entry> constructionEntries = GetConstructionDataEntries(motor, control, constructionCode);
            hasSources = constructionEntries.Count > 0;
            return constructionEntries;
        }

        List<DictionarySource> sources;
        switch (category)
        {
            case Category.Storage:
                sources = GetSingleDictionary(motor, "misStorage");
                break;
            case Category.Inventory:
                sources = GetSingleDictionary(motor, "misRecursosAcumulablesX");
                break;
            case Category.ConstructionStorage:
                sources = GetConstructionDictionaries(motor, "guarda", control);
                break;
            case Category.ConstructionUnlock:
                sources = GetConstructionDictionaries(motor, "desbloquea", control);
                break;
            case Category.ConstructionVariables:
                sources = GetConstructionDictionaries(motor, "proVa", control);
                break;
            case Category.ResourceRate:
                sources = GetSingleDictionary(motor, "misRecursosAcumulablesTasa");
                break;
            case Category.ResourceRateX:
                sources = GetSingleDictionary(motor, "misRecursosAcumulablesTasaX");
                break;
            case Category.GrossResourceRate:
                sources = GetSingleDictionary(motor, "misRecursosAcumulablesTasaBruta");
                break;
            case Category.GlobalVariables:
                sources = GetSingleDictionary(motor, "misVariablesGlobales");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown resource category.");
        }

        hasSources = sources.Count > 0;
        return hasSources
            ? GetDictionaryItems(sources, category == Category.GlobalVariables)
            : new List<Entry>();
    }

    public static List<ConstructionChoice> GetConstructionChoices(Motor motor, Control control)
    {
        object constructions = GetDeepField(motor, "ConstruccionesTodas");
        var choices = new List<ConstructionChoice>();
        if (!(constructions is IEnumerable enumerable)) return choices;

        foreach (object construction in enumerable)
        {
            if (construction == null) continue;

            string codeName = Convert.ToString(GetDeepField(construction, "noCod"), CultureInfo.CurrentCulture) ?? string.Empty;
            if (string.IsNullOrEmpty(codeName)) continue;

            string name = Convert.ToString(GetDeepField(construction, "no"), CultureInfo.CurrentCulture) ?? codeName;
            if (control != null)
            {
                string localizedName = control.NombreConstru(codeName);
                if (!string.IsNullOrEmpty(localizedName)) name = localizedName;
            }

            choices.Add(new ConstructionChoice { Name = name, CodeName = codeName });
        }

        choices.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        return choices;
    }

    private static List<Entry> GetConstructionDataEntries(Motor motor, Control control, string constructionCode)
    {
        object constructions = GetDeepField(motor, "ConstruccionesTodas");
        var entries = new List<Entry>();
        if (string.IsNullOrWhiteSpace(constructionCode) || !(constructions is IEnumerable enumerable))
        {
            return entries;
        }

        foreach (object construction in enumerable)
        {
            if (construction == null) continue;

            string codeName = Convert.ToString(GetDeepField(construction, "noCod"), CultureInfo.CurrentCulture) ?? string.Empty;
            if (!string.Equals(codeName, constructionCode, StringComparison.Ordinal)) continue;

            string sourceName = Convert.ToString(GetDeepField(construction, "no"), CultureInfo.CurrentCulture) ?? codeName;
            if (control != null)
            {
                string localizedName = control.NombreConstru(codeName);
                if (!string.IsNullOrEmpty(localizedName)) sourceName = localizedName;
            }

            var visited = new HashSet<object>(new ObjectReferenceComparer());
            foreach (FieldInfo field in GetInstanceFields(construction.GetType()))
            {
                if (field.IsStatic || field.IsNotSerialized) continue;
                object value = field.GetValue(construction);
                Type fieldType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                string categoryName = field.Name == "proVa" ? "局部变量" : field.Name;
                string keyPath = IsNumericType(fieldType) || field.Name == "proVa"
                    ? string.Empty
                    : field.Name;
                AddEditableValues(value, construction, field, sourceName, categoryName, keyPath, 3, visited, entries);
            }
            break;
        }

        entries.Sort((left, right) =>
        {
            int categoryOrder = StringComparer.CurrentCultureIgnoreCase.Compare(left.CategoryName, right.CategoryName);
            return categoryOrder != 0
                ? categoryOrder
                : StringComparer.CurrentCultureIgnoreCase.Compare(left.KeyName, right.KeyName);
        });
        return entries;
    }

    private static void AddEditableValues(
        object value,
        object owner,
        object key,
        string sourceName,
        string categoryName,
        string keyPath,
        int depth,
        HashSet<object> visited,
        List<Entry> entries)
    {
        if (value == null) return;

        Type valueType = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (IsNumericType(valueType))
        {
            Action<object> setter = CreateSetter(owner, key);
            if (setter == null) return;

            entries.Add(new Entry
            {
                Dictionary = owner,
                Key = key,
                Value = value,
                ValueType = valueType,
                Setter = setter,
                SourceName = sourceName,
                CategoryName = categoryName,
                KeyName = keyPath
            });
            return;
        }

        if (depth <= 0 || value is string || value is Type || valueType.IsPrimitive || valueType.IsEnum ||
            valueType.Namespace == "UnityEngine" || valueType.Namespace == "UnityEngine.UI")
        {
            return;
        }

        if (valueType.IsValueType || !visited.Add(value)) return;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry item in dictionary)
            {
                string itemName = GetKeyName(item.Key);
                string itemPath = $"{keyPath}[{itemName}]";
                AddEditableValues(
                    item.Value,
                    value,
                    item.Key,
                    sourceName,
                    categoryName,
                    itemPath,
                    depth - 1,
                    visited,
                    entries);
            }
            return;
        }

        if (value is IEnumerable enumerable)
        {
            int index = 0;
            foreach (object item in enumerable)
            {
                if (item == null)
                {
                    index++;
                    continue;
                }

                string itemPath = $"{keyPath}[{index}]";
                if (TryGetKeyValue(item, out object itemKey, out object itemValue))
                {
                    string itemName = GetKeyName(itemKey);
                    itemPath = $"{keyPath}[{itemName}]";
                    AddEditableValues(
                        itemValue,
                        value,
                        itemKey,
                        sourceName,
                        categoryName,
                        itemPath,
                        depth - 1,
                        visited,
                        entries);
                }
                else
                {
                    object setterOwner = value is IList ? value : item;
                    object setterKey = value is IList ? (object)index : null;
                    AddEditableValues(
                        item,
                        setterOwner,
                        setterKey,
                        sourceName,
                        categoryName,
                        itemPath,
                        depth - 1,
                        visited,
                        entries);
                }
                index++;
            }
            return;
        }

        foreach (FieldInfo field in GetInstanceFields(valueType))
        {
            if (field.IsStatic || field.IsNotSerialized) continue;
            object fieldValue = field.GetValue(value);
            AddEditableValues(
                fieldValue,
                value,
                field,
                sourceName,
                categoryName,
                $"{keyPath}.{field.Name}",
                depth - 1,
                visited,
                entries);
        }
    }

    private static bool TryGetKeyValue(object item, out object key, out object value)
    {
        if (item is DictionaryEntry dictionaryEntry)
        {
            key = dictionaryEntry.Key;
            value = dictionaryEntry.Value;
            return true;
        }

        Type itemType = item.GetType();
        PropertyInfo keyProperty = itemType.GetProperty("Key");
        PropertyInfo valueProperty = itemType.GetProperty("Value");
        if (keyProperty == null || valueProperty == null)
        {
            key = null;
            value = null;
            return false;
        }

        key = keyProperty.GetValue(item, null);
        value = valueProperty.GetValue(item, null);
        return true;
    }

    private static Action<object> CreateSetter(object owner, object key)
    {
        if (owner == null || key == null) return null;
        if (key is FieldInfo field)
        {
            if (field.IsInitOnly || field.IsLiteral) return null;
            return value => field.SetValue(owner, value);
        }

        if (owner is IList list && key is int)
        {
            return value => list[(int)key] = value;
        }

        if (owner is IDictionary dictionary)
        {
            return value => dictionary[key] = value;
        }

        PropertyInfo indexer = owner.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (indexer == null || indexer.SetMethod == null) return null;
        return value => indexer.SetValue(owner, value, new[] { key });
    }

    private static List<FieldInfo> GetInstanceFields(Type type)
    {
        var fields = new List<FieldInfo>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        for (Type current = type; current != null; current = current.BaseType)
        {
            fields.AddRange(current.GetFields(flags));
        }
        return fields;
    }

    private static bool IsNumericType(Type type)
    {
        TypeCode typeCode = Type.GetTypeCode(type);
        return typeCode == TypeCode.SByte || typeCode == TypeCode.Byte ||
            typeCode == TypeCode.Int16 || typeCode == TypeCode.UInt16 ||
            typeCode == TypeCode.Int32 || typeCode == TypeCode.UInt32 ||
            typeCode == TypeCode.Int64 || typeCode == TypeCode.UInt64 ||
            typeCode == TypeCode.Single || typeCode == TypeCode.Double ||
            typeCode == TypeCode.Decimal;
    }

    public static List<KeyValuePair<string, string>> GetGameInfo(Motor motor)
    {
        var fields = new[]
        {
            new KeyValuePair<string, string>("模拟步数", "step"),
            new KeyValuePair<string, string>("昼夜时刻", "horaDia"),
            new KeyValuePair<string, string>("地图种子", "semilla"),
            new KeyValuePair<string, string>("人口上限", "poblacionLimite"),
            new KeyValuePair<string, string>("暂停状态", "estaPausado"),
            new KeyValuePair<string, string>("游戏结束", "gameOver")
        };
        var info = new List<KeyValuePair<string, string>>();

        foreach (KeyValuePair<string, string> field in fields)
        {
            object value = GetDeepField(motor, field.Value);
            if (value == null) continue;

            string displayValue;
            if (value is bool)
            {
                displayValue = (bool)value ? "是" : "否";
            }
            else if (value is IFormattable)
            {
                displayValue = ((IFormattable)value).ToString("0.##", CultureInfo.CurrentCulture);
            }
            else
            {
                displayValue = Convert.ToString(value, CultureInfo.CurrentCulture);
            }

            info.Add(new KeyValuePair<string, string>(field.Key, displayValue));
        }

        return info;
    }

    public static List<ConstructionInfo> GetConstructionInfo(Motor motor, Control control)
    {
        object constructions = GetDeepField(motor, "ConstruccionesTodas");
        if (!(constructions is IEnumerable enumerable)) return new List<ConstructionInfo>();

        var constructionObjects = new List<object>();
        var constructionsByCode = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (object construction in enumerable)
        {
            if (construction == null) continue;
            constructionObjects.Add(construction);
            string code = Convert.ToString(GetDeepField(construction, "noCod"), CultureInfo.CurrentCulture) ?? string.Empty;
            if (!string.IsNullOrEmpty(code)) constructionsByCode[code] = construction;
        }

        var records = new List<ConstructionInfo>();
        var detailFields = new[]
        {
            new KeyValuePair<string, string>("建造成本", "pre"),
            new KeyValuePair<string, string>("产出与消耗", "proAc"),
            new KeyValuePair<string, string>("储存容量", "guarda"),
            new KeyValuePair<string, string>("解锁条件", "desbloquea"),
            new KeyValuePair<string, string>("预解锁条件", "preDesbloquea"),
            new KeyValuePair<string, string>("资源需求", "necRec"),
            new KeyValuePair<string, string>("变量需求", "necVa"),
            new KeyValuePair<string, string>("局部变量效果", "proVa"),
            new KeyValuePair<string, string>("建筑类别", "tipo"),
            new KeyValuePair<string, string>("区域限定", "zona"),
            new KeyValuePair<string, string>("开采资源", "extrae"),
            new KeyValuePair<string, string>("开采量", "cuantoExtrae"),
            new KeyValuePair<string, string>("开采距离", "distanciaExtraccion"),
            new KeyValuePair<string, string>("连接要求", "conexion"),
            new KeyValuePair<string, string>("已建数量", "nHechas"),
            new KeyValuePair<string, string>("当前数量", "nHay"),
            new KeyValuePair<string, string>("目标数量", "nObjetivo")
        };

        foreach (object construction in constructionObjects)
        {
            if (construction == null) continue;

            object codeNameValue = GetDeepField(construction, "noCod");
            string codeName = Convert.ToString(codeNameValue, CultureInfo.CurrentCulture) ?? string.Empty;
            object rawNameValue = GetDeepField(construction, "no");
            string name = Convert.ToString(rawNameValue, CultureInfo.CurrentCulture) ?? string.Empty;

            if (control != null && !string.IsNullOrEmpty(codeName))
            {
                string localizedName = control.NombreConstru(codeName);
                if (!string.IsNullOrEmpty(localizedName)) name = localizedName;
            }

            if (string.IsNullOrEmpty(name)) name = codeName;
            object descriptionId = GetDeepField(construction, "cid");
            string description = control == null || descriptionId == null
                ? null
                : control.TextoConstru(Convert.ToInt32(descriptionId, CultureInfo.InvariantCulture));
            if (string.IsNullOrEmpty(description))
            {
                description = Convert.ToString(GetDeepField(construction, "texto"), CultureInfo.CurrentCulture);
            }

            var record = new ConstructionInfo
            {
                Name = name,
                CodeName = codeName,
                Description = description
            };

            foreach (KeyValuePair<string, string> field in detailFields)
            {
                object value = GetDeepField(construction, field.Value);
                if (value != null)
                {
                    record.Details.Add(new ConstructionDetail { Name = field.Key, Value = value });
                }
            }

            records.Add(record);
        }

        var namesByCode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ConstructionInfo record in records)
        {
            if (!string.IsNullOrEmpty(record.CodeName)) namesByCode[record.CodeName] = record.Name;
        }

        var upgradesByCode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> construction in constructionsByCode)
        {
            var upgrades = new List<string>();
            CollectRelatedConstructionCodes(GetDeepField(construction.Value, "updates"), upgrades);
            CollectRelatedConstructionCodes(GetDeepField(construction.Value, "evoluciona"), upgrades);
            upgrades.RemoveAll(code =>
                string.IsNullOrEmpty(code) ||
                string.Equals(code, construction.Key, StringComparison.Ordinal));
            upgradesByCode[construction.Key] = upgrades;
        }

        var prerequisitesByCode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> construction in constructionsByCode)
        {
            var prerequisites = new List<string>();
            CollectPrerequisiteCodes(GetDeepField(construction.Value, "preDesbloquea"), prerequisites);
            CollectPrerequisiteCodes(GetDeepField(construction.Value, "desbloquea"), prerequisites);
            prerequisitesByCode[construction.Key] = prerequisites;
        }

        foreach (KeyValuePair<string, List<string>> upgrade in upgradesByCode)
        {
            foreach (string upgradedCode in upgrade.Value)
            {
                if (!prerequisitesByCode.TryGetValue(upgradedCode, out List<string> prerequisites)) continue;
                if (!prerequisites.Contains(upgrade.Key)) prerequisites.Add(upgrade.Key);
            }
        }

        Dictionary<string, int> prerequisiteWeights = GetPrerequisiteWeights(
            prerequisitesByCode,
            namesByCode);
        foreach (ConstructionInfo record in records)
        {
            string prerequisiteTree = GetPrerequisiteBuildingTree(
                record.CodeName,
                prerequisitesByCode,
                namesByCode,
                prerequisiteWeights);
            if (!string.IsNullOrEmpty(prerequisiteTree))
            {
                record.Details.Add(new ConstructionDetail
                {
                    Name = "前置建筑链",
                    Value = prerequisiteTree
                });
            }
        }

        records.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        return records;
    }

    private static string GetPrerequisiteBuildingTree(
        string codeName,
        Dictionary<string, List<string>> prerequisitesByCode,
        Dictionary<string, string> namesByCode,
        Dictionary<string, int> prerequisiteWeights)
    {
        if (string.IsNullOrEmpty(codeName) ||
            !namesByCode.ContainsKey(codeName) ||
            !prerequisiteWeights.TryGetValue(codeName, out int ownWeight))
        {
            return null;
        }

        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<KeyValuePair<string, int>>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { codeName };
        pending.Enqueue(new KeyValuePair<string, int>(codeName, 0));
        while (pending.Count > 0)
        {
            KeyValuePair<string, int> current = pending.Dequeue();
            if (current.Value >= MaxPrerequisiteChainDepth) continue;
            if (!prerequisitesByCode.TryGetValue(current.Key, out List<string> parents)) continue;

            foreach (string parent in parents)
            {
                if (namesByCode.ContainsKey(parent) &&
                    prerequisiteWeights.TryGetValue(parent, out int parentWeight) &&
                    parentWeight < ownWeight &&
                    ancestors.Count < MaxPrerequisiteNodesVisited &&
                    visited.Add(parent))
                {
                    ancestors.Add(parent);
                    pending.Enqueue(new KeyValuePair<string, int>(parent, current.Value + 1));
                }
            }
        }

        if (ancestors.Count < 2) return null;

        var levels = new SortedDictionary<int, List<string>>();
        foreach (string ancestor in ancestors)
        {
            int weight = prerequisiteWeights[ancestor];
            if (!levels.TryGetValue(weight, out List<string> buildings))
            {
                buildings = new List<string>();
                levels.Add(weight, buildings);
            }

            List<string> immediateParents = prerequisitesByCode.TryGetValue(ancestor, out List<string> parents)
                ? parents.FindAll(parent =>
                    ancestors.Contains(parent) &&
                    prerequisiteWeights.TryGetValue(parent, out int parentWeight) &&
                    parentWeight == weight - 1)
                : new List<string>();
            immediateParents.Sort((left, right) =>
                StringComparer.CurrentCultureIgnoreCase.Compare(namesByCode[left], namesByCode[right]));

            string displayName = namesByCode[ancestor];
            if (immediateParents.Count > 0)
            {
                var parentNames = new List<string>();
                foreach (string parent in immediateParents)
                {
                    string parentName = namesByCode[parent];
                    if (!parentNames.Contains(parentName)) parentNames.Add(parentName);
                }
                displayName += $"（前置：{string.Join("、", parentNames)}）";
            }
            buildings.Add(displayName);
        }

        var lines = new List<string>();
        foreach (KeyValuePair<int, List<string>> level in levels)
        {
            level.Value.Sort(StringComparer.CurrentCultureIgnoreCase);
            string prefix = level.Key == 0 ? "├─" : new string(' ', Math.Min(level.Key - 1, 8) * 2) + "└─";
            lines.Add($"{prefix} 权重 {level.Key}：{string.Join("、", level.Value)}");
        }
        return string.Join("\n", lines);
    }

    private static Dictionary<string, int> GetPrerequisiteWeights(
        Dictionary<string, List<string>> prerequisitesByCode,
        Dictionary<string, string> namesByCode)
    {
        var childrenByCode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var roots = new List<string>();
        foreach (string code in namesByCode.Keys)
        {
            childrenByCode[code] = new List<string>();
        }

        foreach (string child in namesByCode.Keys)
        {
            bool hasParent = false;
            if (prerequisitesByCode.TryGetValue(child, out List<string> parents))
            {
                foreach (string parent in parents)
                {
                    if (!childrenByCode.TryGetValue(parent, out List<string> children)) continue;
                    hasParent = true;
                    children.Add(child);
                }
            }
            if (!hasParent) roots.Add(child);
        }

        var weights = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (string root in roots)
        {
            weights[root] = 0;
            queue.Enqueue(root);
        }

        PropagatePrerequisiteWeights(childrenByCode, weights, queue);
        while (weights.Count < namesByCode.Count)
        {
            string cycleRoot = null;
            foreach (string code in namesByCode.Keys)
            {
                if (!weights.ContainsKey(code) &&
                    (cycleRoot == null || StringComparer.Ordinal.Compare(code, cycleRoot) < 0))
                {
                    cycleRoot = code;
                }
            }
            if (cycleRoot == null) break;
            weights[cycleRoot] = 0;
            queue.Enqueue(cycleRoot);
            PropagatePrerequisiteWeights(childrenByCode, weights, queue);
        }

        return weights;
    }

    private static void PropagatePrerequisiteWeights(
        Dictionary<string, List<string>> childrenByCode,
        Dictionary<string, int> weights,
        Queue<string> queue)
    {
        while (queue.Count > 0)
        {
            string parent = queue.Dequeue();
            int childWeight = weights[parent] + 1;
            foreach (string child in childrenByCode[parent])
            {
                if (weights.ContainsKey(child)) continue;

                weights[child] = childWeight;
                queue.Enqueue(child);
            }
        }
    }

    private static void CollectPrerequisiteCodes(object requirements, List<string> codes)
    {
        if (requirements == null) return;

        if (requirements is IDictionary dictionary)
        {
            foreach (DictionaryEntry item in dictionary)
            {
                AddPrerequisiteCode(item.Key, codes);
                AddPrerequisiteCode(item.Value, codes);
            }
            return;
        }

        if (requirements is IEnumerable enumerable && !(requirements is string))
        {
            foreach (object requirement in enumerable)
            {
                AddPrerequisiteCode(requirement, codes);
                AddPrerequisiteCode(GetDeepField(requirement, "Key"), codes);
            }
            return;
        }

        AddPrerequisiteCode(requirements, codes);
    }

    private static void CollectRelatedConstructionCodes(object relatedConstructions, List<string> codes)
    {
        if (relatedConstructions == null) return;

        if (relatedConstructions is IEnumerable enumerable && !(relatedConstructions is string))
        {
            foreach (object relatedConstruction in enumerable)
            {
                AddRelatedConstructionCode(relatedConstruction, codes);
            }
            return;
        }

        AddRelatedConstructionCode(relatedConstructions, codes);
    }

    private static void AddRelatedConstructionCode(object relatedConstruction, List<string> codes)
    {
        if (relatedConstruction == null) return;

        object code = GetDeepField(relatedConstruction, "noCod");
        string codeName = Convert.ToString(code ?? relatedConstruction, CultureInfo.CurrentCulture);
        if (!string.IsNullOrEmpty(codeName) && !codes.Contains(codeName))
        {
            codes.Add(codeName);
        }
    }

    private static void AddPrerequisiteCode(object requirement, List<string> codes)
    {
        object code = GetDeepField(requirement, "nBuildings");
        if (code == null) return;

        object codeNameValue = GetDeepField(code, "noCod");
        string codeName = Convert.ToString(codeNameValue ?? code, CultureInfo.CurrentCulture);
        if (!string.IsNullOrEmpty(codeName) && !codes.Contains(codeName))
        {
            codes.Add(codeName);
        }
    }

    public static string FormatValue(object value, Func<string, string> translate, Control control = null)
    {
        if (value == null) return "无数据";
        if (value is string) return translate((string)value);
        if (value is bool) return (bool)value ? "是" : "否";
        if (value is IFormattable)
        {
            return ((IFormattable)value).ToString("0.##", CultureInfo.CurrentCulture);
        }

        if (value is IEnumerable enumerable)
        {
            var values = new List<string>();
            foreach (object item in enumerable)
            {
                if (item == null) continue;

                Type itemType = item.GetType();
                PropertyInfo keyProperty = itemType.GetProperty("Key");
                PropertyInfo valueProperty = itemType.GetProperty("Value");
                if (keyProperty != null && valueProperty != null)
                {
                    object key = keyProperty.GetValue(item, null);
                    object itemValue = valueProperty.GetValue(item, null);
                    values.Add($"{translate(GetKeyName(key))}：{FormatValue(itemValue, translate, control)}");
                    continue;
                }

                string formattedItem = FormatRequirement(item, translate, control);
                values.Add(string.IsNullOrEmpty(formattedItem)
                    ? "无数据"
                    : formattedItem);
            }

            return values.Count == 0 ? "无数据" : string.Join("；", values);
        }

        string formattedValue = FormatRequirement(value, translate, control);
        return string.IsNullOrEmpty(formattedValue) ? "无数据" : formattedValue;
    }

    private static string FormatRequirement(object item, Func<string, string> translate, Control control)
    {
        var parts = new List<string>();
        var fields = new[]
        {
            new KeyValuePair<string, string>("rec", "资源"),
            new KeyValuePair<string, string>("gloVar", "全局变量"),
            new KeyValuePair<string, string>("nBuildings", "建筑"),
            new KeyValuePair<string, string>("building", "建筑"),
            new KeyValuePair<string, string>("locVar", "局部变量"),
            new KeyValuePair<string, string>("localVariable", "局部变量"),
            new KeyValuePair<string, string>("nombre", "名称"),
            new KeyValuePair<string, string>("nombreCodificado", "变量代码"),
            new KeyValuePair<string, string>("q", "数量"),
            new KeyValuePair<string, string>("radio", "范围"),
            new KeyValuePair<string, string>("biggerThan", "大于"),
            new KeyValuePair<string, string>("resource", "资源"),
            new KeyValuePair<string, string>("value", "数值")
        };

        foreach (KeyValuePair<string, string> field in fields)
        {
            object value = GetDeepField(item, field.Key);
            if (value != null)
            {
                string text = FormatRequirementValue(
                    value,
                    translate,
                    control,
                    field.Key == "nBuildings" || field.Key == "building");
                parts.Add($"{field.Value}：{text}");
            }
        }

        object variableName = GetDeepField(item, "nombre");
        if (variableName == null) variableName = GetDeepField(item, "nombreCodificado");
        if (variableName != null)
        {
            string name = Convert.ToString(variableName, CultureInfo.CurrentCulture);
            if (parts.Count == 0 || !parts.Exists(part => part.Contains(translate(name))))
            {
                parts.Insert(0, translate(name));
            }
        }

        if (parts.Count == 0)
        {
            foreach (FieldInfo field in GetInstanceFields(item.GetType()))
            {
                if (field.IsStatic || field.IsNotSerialized) continue;
                object value = field.GetValue(item);
                if (value == null) continue;
                parts.Add($"{translate(field.Name)}：{FormatRequirementValue(value, translate, control, false)}");
            }
        }

        return string.Join("，", parts);
    }

    private static string FormatRequirementValue(
        object value,
        Func<string, string> translate,
        Control control,
        bool isConstruction)
    {
        if (value == null) return "无数据";

        string rawText = Convert.ToString(value, CultureInfo.CurrentCulture);
        object variableName = GetDeepField(value, "nombre");
        if (variableName == null) variableName = GetDeepField(value, "nombreCodificado");
        if (variableName != null)
        {
            rawText = Convert.ToString(variableName, CultureInfo.CurrentCulture);
        }

        if (isConstruction && control != null)
        {
            string localizedName = control.NombreConstru(rawText);
            if (!string.IsNullOrEmpty(localizedName)) return localizedName;
        }

        if (variableName != null) return translate(rawText);
        if (value is string) return translate((string)value);
        if (value is bool) return (bool)value ? "是" : "否";
        if (value is IFormattable)
        {
            return ((IFormattable)value).ToString("0.##", CultureInfo.CurrentCulture);
        }

        return translate(Convert.ToString(value, CultureInfo.CurrentCulture));
    }

    public static void ApplyValue(List<Entry> entries, object dictionary, object targetKey, string inputValue, string selectedName)
    {
        if (targetKey == null)
        {
            Plugin.Log.LogWarning("请先在列表中点击选择一个资源！");
            return;
        }

        Entry selectedEntry = null;
        foreach (Entry entry in entries)
        {
            if (ReferenceEquals(entry.Dictionary, dictionary) && Equals(entry.Key, targetKey))
            {
                selectedEntry = entry;
                break;
            }
        }

        if (selectedEntry == null)
        {
            Plugin.Log.LogWarning($"当前列表中已找不到键：{selectedName}");
            return;
        }

        if (selectedEntry.Setter != null)
        {
            Type dynamicValueType = selectedEntry.ValueType;
            if (!IsNumericType(dynamicValueType))
            {
                Plugin.Log.LogWarning($"不支持修改此类型的值：{dynamicValueType.FullName}");
                return;
            }

            object dynamicConvertedValue;
            try
            {
                dynamicConvertedValue = Convert.ChangeType(inputValue, dynamicValueType, CultureInfo.InvariantCulture);
            }
            catch (InvalidCastException)
            {
                Plugin.Log.LogWarning($"不支持修改此类型的值：{dynamicValueType.FullName}");
                return;
            }
            catch (FormatException)
            {
                Plugin.Log.LogWarning($"无法将输入值转换为 {dynamicValueType.FullName}。");
                return;
            }
            catch (OverflowException)
            {
                Plugin.Log.LogWarning($"输入值超出 {dynamicValueType.FullName} 的范围。");
                return;
            }

            selectedEntry.Setter(dynamicConvertedValue);
            Plugin.Log.LogInfo($"已修改 {selectedName} => {Convert.ToString(dynamicConvertedValue, CultureInfo.InvariantCulture)}");
            return;
        }

        PropertyInfo indexer = dictionary.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (indexer == null || indexer.SetMethod == null)
        {
            Plugin.Log.LogWarning($"无法写入此字典类型：{dictionary.GetType().FullName}");
            return;
        }

        Type valueType = Nullable.GetUnderlyingType(indexer.PropertyType) ?? indexer.PropertyType;
        TypeCode typeCode = Type.GetTypeCode(valueType);
        if (typeCode != TypeCode.SByte && typeCode != TypeCode.Byte &&
            typeCode != TypeCode.Int16 && typeCode != TypeCode.UInt16 &&
            typeCode != TypeCode.Int32 && typeCode != TypeCode.UInt32 &&
            typeCode != TypeCode.Int64 && typeCode != TypeCode.UInt64 &&
            typeCode != TypeCode.Single && typeCode != TypeCode.Double &&
            typeCode != TypeCode.Decimal)
        {
            Plugin.Log.LogWarning($"不支持修改此类型的值：{valueType.FullName}");
            return;
        }

        object convertedValue;
        try
        {
            convertedValue = Convert.ChangeType(inputValue, valueType, CultureInfo.InvariantCulture);
        }
        catch (InvalidCastException)
        {
            Plugin.Log.LogWarning($"不支持修改此类型的值：{valueType.FullName}");
            return;
        }
        catch (FormatException)
        {
            Plugin.Log.LogWarning($"无法将输入值转换为 {valueType.FullName}。");
            return;
        }
        catch (OverflowException)
        {
            Plugin.Log.LogWarning($"输入值超出 {valueType.FullName} 的范围。");
            return;
        }

        SetDictionaryValue(dictionary, targetKey, convertedValue);
        if (selectedEntry.ReapplyAfterUpdate)
        {
            RegisterGlobalVariableOverride(dictionary, targetKey, convertedValue);
        }
        Plugin.Log.LogInfo($"已修改 {selectedName} => {Convert.ToString(convertedValue, CultureInfo.InvariantCulture)}");
    }

    public static void ReapplyGlobalVariableOverrides(Motor motor)
    {
        if (motor == null || GlobalVariableOverrides.Count == 0) return;

        object dictionary = GetDeepField(motor, "misVariablesGlobales");
        if (dictionary == null) return;

        for (int i = GlobalVariableOverrides.Count - 1; i >= 0; i--)
        {
            GlobalVariableOverride item = GlobalVariableOverrides[i];
            if (!ReferenceEquals(item.Dictionary, dictionary)) continue;

            try
            {
                SetDictionaryValue(item.Dictionary, item.Key, item.Value);
            }
            catch (InvalidOperationException exception)
            {
                LogGlobalOverrideFailure(item, exception);
                GlobalVariableOverrides.RemoveAt(i);
            }
            catch (TargetInvocationException exception)
            {
                LogGlobalOverrideFailure(item, exception);
                GlobalVariableOverrides.RemoveAt(i);
            }
            catch (ArgumentException exception)
            {
                LogGlobalOverrideFailure(item, exception);
                GlobalVariableOverrides.RemoveAt(i);
            }
            catch (MethodAccessException exception)
            {
                LogGlobalOverrideFailure(item, exception);
                GlobalVariableOverrides.RemoveAt(i);
            }
        }
    }

    public static void ClearGlobalVariableOverrides()
    {
        GlobalVariableOverrides.Clear();
    }

    private static void LogGlobalOverrideFailure(GlobalVariableOverride item, Exception exception)
    {
        Plugin.Log.LogError($"每帧重写全局变量失败（{GetKeyName(item.Key)}）：{exception}");
    }

    private static void RegisterGlobalVariableOverride(object dictionary, object key, object value)
    {
        foreach (GlobalVariableOverride item in GlobalVariableOverrides)
        {
            if (ReferenceEquals(item.Dictionary, dictionary) && Equals(item.Key, key))
            {
                item.Value = value;
                return;
            }
        }

        GlobalVariableOverrides.Add(new GlobalVariableOverride
        {
            Dictionary = dictionary,
            Key = key,
            Value = value
        });
    }

    private static void SetDictionaryValue(object dictionary, object key, object value)
    {
        PropertyInfo indexer = dictionary.GetType().GetProperty(
            "Item",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (indexer == null || indexer.SetMethod == null)
        {
            throw new InvalidOperationException($"无法写入此字典类型：{dictionary.GetType().FullName}");
        }

        indexer.SetValue(dictionary, value, new[] { key });
    }

    public static void PrintCurrentList(List<Entry> entries, string title)
    {
        Plugin.Log.LogInfo($"===== 打印所有【{title}】 =====");
        foreach (Entry entry in entries)
        {
            Plugin.Log.LogInfo($"{entry.SourceName} / {entry.KeyName} : {entry.Value}");
        }
    }

    private static List<DictionarySource> GetSingleDictionary(Motor motor, string fieldName)
    {
        object dictionary = GetDeepField(motor, fieldName);
        if (dictionary == null) return new List<DictionarySource>();
        return new List<DictionarySource>
        {
            new DictionarySource { Dictionary = dictionary, Name = fieldName }
        };
    }

    private static List<DictionarySource> GetConstructionDictionaries(Motor motor, string dictionaryFieldName, Control control)
    {
        object constructions = GetDeepField(motor, "ConstruccionesTodas");
        if (!(constructions is IEnumerable enumerable)) return new List<DictionarySource>();

        var sources = new List<DictionarySource>();
        foreach (object construction in enumerable)
        {
            if (construction == null) continue;

            object dictionary = GetDeepField(construction, dictionaryFieldName);
            if (dictionary == null) continue;

            string codeName = Convert.ToString(GetDeepField(construction, "noCod"), CultureInfo.CurrentCulture) ?? string.Empty;
            object constructionName = GetDeepField(construction, "no");
            string name = constructionName == null ? codeName : constructionName.ToString();
            if (control != null && !string.IsNullOrEmpty(codeName))
            {
                string localizedName = control.NombreConstru(codeName);
                if (!string.IsNullOrEmpty(localizedName)) name = localizedName;
            }
            sources.Add(new DictionarySource { Dictionary = dictionary, Name = name });
        }

        return sources;
    }

    private static List<Entry> GetDictionaryItems(List<DictionarySource> sources, bool reapplyAfterUpdate)
    {
        var entries = new List<Entry>();
        foreach (DictionarySource source in sources)
        {
            if (!(source.Dictionary is IEnumerable enumerable)) continue;
            foreach (object item in enumerable)
            {
                object key;
                object value;
                if (item is DictionaryEntry dictionaryEntry)
                {
                    key = dictionaryEntry.Key;
                    value = dictionaryEntry.Value;
                }
                else if (item != null)
                {
                    Type itemType = item.GetType();
                    PropertyInfo keyProperty = itemType.GetProperty("Key");
                    PropertyInfo valueProperty = itemType.GetProperty("Value");
                    if (keyProperty == null || valueProperty == null) return null;
                    key = keyProperty.GetValue(item, null);
                    value = valueProperty.GetValue(item, null);
                }
                else
                {
                    continue;
                }

                entries.Add(new Entry
                {
                    Key = key,
                    Value = value,
                    Dictionary = source.Dictionary,
                    KeyName = GetKeyName(key),
                    SourceName = source.Name,
                    ReapplyAfterUpdate = reapplyAfterUpdate
                });
            }
        }

        entries.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.SourceName + left.KeyName, right.SourceName + right.KeyName));
        return entries;
    }

    private static string GetKeyName(object key)
    {
        if (key == null) return "null";
        object name = GetDeepField(key, "nombre");
        return name == null ? key.ToString() : name.ToString();
    }

    private static object GetDeepField(object obj, string path)
    {
        if (obj == null || string.IsNullOrEmpty(path)) return null;

        object current = obj;
        foreach (string part in path.Split('.'))
        {
            if (current == null) return null;

            Type type = current.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo field = type.GetField(part, flags);
            if (field != null)
            {
                current = field.GetValue(current);
                continue;
            }

            PropertyInfo property = type.GetProperty(part, flags);
            if (property == null) return null;
            current = property.GetValue(current, null);
        }

        return current;
    }
}
