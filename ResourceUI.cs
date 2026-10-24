using ResourceDebugMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

public class ResourceUI
{
    // ======================= UI 基础字段 =======================
    private readonly Plugin _plugin;
    private Rect _windowRect = new Rect(80, 80, 760, 700);
    private Vector2 _scrollPos;
    private Vector2 _listScrollPos;
    private Vector2 _encyclopediaListScrollPos;
    private Vector2 _encyclopediaDetailScrollPos;
    private Vector2 _constructionChoiceScrollPos;
    private bool _foldResource = true;
    private bool _foldEncyclopedia;
    private bool _foldAchievement;
    private string _achievementId = "";
    private string _encyclopediaSearch = "";
    private string _selectedConstructionCode = "";
    private string _constructionSearch = "";
    private string _lastConstructionSearch = "";
    private Motor _encyclopediaMotor;
    private Control _encyclopediaControl;
    private List<ResourceData.ConstructionInfo> _encyclopediaRecords;
    private Motor _constructionChoicesMotor;
    private Control _constructionChoicesControl;
    private List<ResourceData.ConstructionChoice> _constructionChoices;

    // 窗口固定缩放
    private float _designWidth = 760f;
    private float _designHeight = 700f;
    private static readonly float[] UiScales = { 1f, 1.5f, 2f };
    private int _scaleIndex;
    private float _uiScale = 1f;

    // 翻译系统
    private Dictionary<string, string> _lang = new Dictionary<string, string>();
    private bool _langLoaded = false;
    private const string _langFileName = "lang.ini";

    // ======================= 数据驱动 Tab 系统 =======================
    private int _currentTabIndex = 0;

    // 记录不同字典的选中项和输入值
    private Dictionary<int, object> _selectedKeys = new Dictionary<int, object>();
    private Dictionary<int, object> _selectedDictionaries = new Dictionary<int, object>();
    private Dictionary<int, string> _inputValues = new Dictionary<int, string>();
    private Dictionary<int, string> _savedNames = new Dictionary<int, string>();
    private Dictionary<int, string> _searchTexts = new Dictionary<int, string>();

    /// <summary>
    /// 单个标签页的配置信息（只需要在这里加，界面和逻辑自动生成）
    /// </summary>
    private class TabConfig
    {
        public string TabName;
        public string Description;
        public string TipText;
        public ResourceData.Category Category;
    }

    private List<TabConfig> _configs = new List<TabConfig>
    {
        new TabConfig { TabName = "仓库", Description = "查看并修改当前城市仓库中各类资源的库存数量。", TipText = "👉 选中一个仓库资源：", Category = ResourceData.Category.Storage },
        new TabConfig { TabName = "背包", Description = "查看并修改玩家背包中可累积资源的当前数量。", TipText = "👉 选中一个背包资源：", Category = ResourceData.Category.Inventory },
        new TabConfig { TabName = "储存", Description = "查看建筑定义的资源储存容量；同一种资源可能出现在多个建筑上。", TipText = "👉 所有建筑定义的储存资源：", Category = ResourceData.Category.ConstructionStorage },
        new TabConfig { TabName = "解锁", Description = "查看建筑相关的解锁要求，可用于检查或调整解锁状态。", TipText = "👉 所有建筑定义的解锁进度：", Category = ResourceData.Category.ConstructionUnlock },
        new TabConfig { TabName = "局部变量", Description = "查看建筑产生的局部变量效果（proVa），例如对周围建筑或区域的影响。", TipText = "👉 各建筑的局部变量效果（proVa）：", Category = ResourceData.Category.ConstructionVariables },
        new TabConfig { TabName = "产率", Description = "查看游戏当前记录的资源净变化速率。", TipText = "👉 游戏记录的资源速率（misRecursosAcumulablesTasa）：", Category = ResourceData.Category.ResourceRate },
        new TabConfig { TabName = "产率X", Description = "查看扩展资源集合中的资源变化速率记录。", TipText = "👉 游戏记录的资源速率 X（misRecursosAcumulablesTasaX）：", Category = ResourceData.Category.ResourceRateX },
        new TabConfig { TabName = "净产率", Description = "查看资源的毛产出速率，不扣除同一资源的消耗。", TipText = "👉 游戏记录的资源毛速率（misRecursosAcumulablesTasaBruta）：", Category = ResourceData.Category.GrossResourceRate },
        new TabConfig { TabName = "全局", Description = "查看并修改影响整个城市或游戏状态的全局变量；手动修改值会在每帧更新后重新写入。", TipText = "👉 游戏全局变量（misVariablesGlobales）：", Category = ResourceData.Category.GlobalVariables },
        new TabConfig { TabName = "动态数据", Description = "按需搜索单个建筑，查看并修改其原版定义中的数值字段和局部变量，不会预先加载所有建筑的动态字段。", TipText = "👉 搜索并选择建筑后查看其原版字段和局部变量：", Category = ResourceData.Category.ConstructionData }
    };
    public ResourceUI(Plugin plugin)
    {
        _plugin = plugin;
    }

    public void Draw()
    {
        if (!_plugin.WindowOpen) return;

        Matrix4x4 oldMatrix = GUI.matrix;
        // ✅ 在 Window 外面整体缩放整个窗口，而不是窗口里面缩放内容
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * _uiScale);

        // 窗口基准 rect 永远用原始设计尺寸，不乘缩放！
        Rect windowBaseRect = new Rect(
            _windowRect.x / _uiScale,
            _windowRect.y / _uiScale,
            _designWidth,
            _designHeight
        );

        windowBaseRect = GUI.Window(9999, windowBaseRect, DrawPanel, $"资源修改器 (x{_uiScale:F2})");

        // 同步回真实屏幕坐标
        _windowRect.x = windowBaseRect.x * _uiScale;
        _windowRect.y = windowBaseRect.y * _uiScale;

        GUI.matrix = oldMatrix;
    }

    private void DrawPanel(int id)
    {
        // 删掉：unscaledWidth / unscaledHeight
        // 删掉：BeginGroup
        // 删掉：Matrix4x4 oldMatrix = GUI.matrix; GUI.matrix = ...

        // 直接正常布局，区域就是原始设计大小
        GUILayout.BeginHorizontal("Box");
        GUILayout.Label("拖动此栏移动窗口", GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("界面缩放", GUILayout.Width(70f));
        int selectedScaleIndex = GUILayout.Toolbar(_scaleIndex, new[] { "1.0x", "1.5x", "2.0x" });
        if (selectedScaleIndex != _scaleIndex)
        {
            _scaleIndex = selectedScaleIndex;
            _uiScale = UiScales[_scaleIndex];
            // ✅ 不再修改 _windowRect.width/height！基准尺寸固定 designWidth/Height
        }
        if (GUILayout.Button("重载翻译", GUILayout.Width(88f)))
        {
            ReloadLang();
        }
        GUILayout.EndHorizontal();

        _scrollPos = GUILayout.BeginScrollView(
            _scrollPos,
            false,
            true,
            GUIStyle.none,
            GUI.skin.verticalScrollbar);
        _foldResource = GUILayout.Toggle(_foldResource, "【资源列表】", "Button");

        if (_foldResource)
        {
            GUILayout.BeginVertical("Box");

            if (_plugin.MotorInstance == null)
            {
                GUILayout.Label("❌ 未找到 Motor 实例");
            }
            else
            {
                Motor motor = _plugin.MotorInstance;
                DrawGameInfo(motor);
                DrawAchievementUnlocker();

                string[] tabNames = _configs.ConvertAll(c => c.TabName).ToArray();
                _currentTabIndex = Mathf.Clamp(GUILayout.Toolbar(_currentTabIndex, tabNames, GUILayout.ExpandWidth(true)), 0, _configs.Count - 1);
                GUILayout.Space(6);

                var config = _configs[_currentTabIndex];
                GUILayout.BeginVertical("Box");
                GUILayout.Label($"作用：{config.Description}", GUILayout.ExpandWidth(true));
                GUILayout.EndVertical();
                GUILayout.Label(config.TipText);

                if (config.Category == ResourceData.Category.ConstructionData)
                {
                    string constructionCode = DrawConstructionDataPicker(motor);
                    if (string.IsNullOrEmpty(constructionCode))
                    {
                        GUILayout.Label("请搜索特定建筑（如商住混合）");
                    }
                    else
                    {
                        List<ResourceData.Entry> selectedEntries = ResourceData.GetEntries(
                            motor,
                            config.Category,
                            out bool hasConstructionData,
                            _plugin.ControlInstance,
                            constructionCode);
                        DrawEntrySection(selectedEntries, hasConstructionData, config.TabName, _currentTabIndex);
                    }
                }
                else
                {
                    List<ResourceData.Entry> entries = ResourceData.GetEntries(
                        motor,
                        config.Category,
                        out bool hasSources,
                        _plugin.ControlInstance);
                    DrawEntrySection(entries, hasSources, config.TabName, _currentTabIndex);
                }
            }
            GUILayout.EndVertical();
        }

        if (_plugin.MotorInstance != null)
        {
            DrawEncyclopedia(_plugin.MotorInstance);
        }

        GUILayout.EndScrollView();

        // ✅ DragWindow 直接用窗口原生坐标系，不再除以缩放
        GUI.DragWindow(new Rect(0f, 0f, _designWidth, 26f));
    }

    private string DrawConstructionDataPicker(Motor motor)
    {
        Control control = _plugin.ControlInstance;
        if (!ReferenceEquals(_constructionChoicesMotor, motor) ||
            !ReferenceEquals(_constructionChoicesControl, control) ||
            _constructionChoices == null)
        {
            _constructionChoicesMotor = motor;
            _constructionChoicesControl = control;
            _constructionChoices = ResourceData.GetConstructionChoices(motor, control);
            _selectedConstructionCode = "";
            ClearConstructionDataSelection();
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("搜索建筑", GUILayout.Width(65f));
        _constructionSearch = GUILayout.TextField(_constructionSearch);
        if (GUILayout.Button("清除", GUILayout.Width(48f)))
        {
            _constructionSearch = "";
            _selectedConstructionCode = "";
            ClearConstructionDataSelection();
        }
        GUILayout.EndHorizontal();

        if (!string.Equals(_lastConstructionSearch, _constructionSearch, StringComparison.Ordinal))
        {
            _lastConstructionSearch = _constructionSearch;
            _selectedConstructionCode = "";
            ClearConstructionDataSelection();
        }

        if (string.IsNullOrEmpty(_selectedConstructionCode))
        {
            string search = _constructionSearch.Trim();
            if (string.IsNullOrEmpty(search))
            {
                return null;
            }

            List<ResourceData.ConstructionChoice> matches = _constructionChoices.FindAll(choice =>
                choice.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                choice.CodeName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
            GUILayout.Label($"匹配到 {matches.Count} 栋建筑");
            _constructionChoiceScrollPos = GUILayout.BeginScrollView(
                _constructionChoiceScrollPos,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.Height(130f));
            foreach (ResourceData.ConstructionChoice choice in matches)
            {
                if (GUILayout.Button($"{choice.Name}  [{choice.CodeName}]", GUILayout.ExpandWidth(true)))
                {
                    _selectedConstructionCode = choice.CodeName;
                    ClearConstructionDataSelection();
                }
            }
            GUILayout.EndScrollView();
            return _selectedConstructionCode;
        }

        ResourceData.ConstructionChoice selected = _constructionChoices.Find(
            choice => string.Equals(choice.CodeName, _selectedConstructionCode, StringComparison.Ordinal));
        if (GUILayout.Button($"当前建筑：{(selected == null ? _selectedConstructionCode : selected.Name)}（点击重新搜索）"))
        {
            _selectedConstructionCode = "";
            ClearConstructionDataSelection();
        }
        return _selectedConstructionCode;
    }

    private void ClearConstructionDataSelection()
    {
        int tabIndex = _configs.FindIndex(config => config.Category == ResourceData.Category.ConstructionData);
        if (tabIndex < 0) return;

        EnsureTabState(tabIndex);
        _selectedKeys[tabIndex] = null;
        _selectedDictionaries[tabIndex] = null;
        _savedNames[tabIndex] = "";
        _inputValues[tabIndex] = "0";
    }

    private void DrawEntrySection(List<ResourceData.Entry> entries, bool hasSources, string tabName, int tabIndex)
    {
        if (!hasSources)
        {
            GUILayout.Label($"当前 {tabName}没有可用的数据源。");
            return;
        }
        if (entries == null)
        {
            GUILayout.Label("无法识别当前数据类型。");
            return;
        }

        DrawDictionaryItems(entries, tabIndex);
        GUILayout.Space(8);
        GUILayout.Box("数值修改器", GUILayout.ExpandWidth(true));
        EnsureTabState(tabIndex);
        string selectedName = _savedNames[tabIndex];
        GUILayout.Label(string.IsNullOrEmpty(selectedName) ? "尚未选择资源" : $"当前选择：{selectedName}");
        _inputValues[tabIndex] = GUILayout.TextField(_inputValues[tabIndex]);

        GUILayout.BeginHorizontal();
        bool previousGuiEnabled = GUI.enabled;
        GUI.enabled = _selectedKeys[tabIndex] != null && _selectedDictionaries[tabIndex] != null;
        if (GUILayout.Button("修改选中值"))
        {
            ResourceData.ApplyValue(
                entries,
                _selectedDictionaries[tabIndex],
                _selectedKeys[tabIndex],
                _inputValues[tabIndex],
                _savedNames[tabIndex]);
        }
        GUI.enabled = previousGuiEnabled;
        if (GUILayout.Button("打印当前列表")) ResourceData.PrintCurrentList(entries, tabName);
        GUILayout.EndHorizontal();
    }

    private void DrawEncyclopedia(Motor motor)
    {
        _foldEncyclopedia = GUILayout.Toggle(_foldEncyclopedia, "【建筑百科】", "Button");
        if (!_foldEncyclopedia) return;

        if (!ReferenceEquals(_encyclopediaMotor, motor) ||
            !ReferenceEquals(_encyclopediaControl, _plugin.ControlInstance) ||
            _encyclopediaRecords == null)
        {
            RefreshEncyclopedia(motor);
        }

        GUILayout.BeginVertical("Box");
        GUILayout.Label($"读取原版建筑数据：共 {_encyclopediaRecords.Count} 条。");

        GUILayout.BeginHorizontal();
        GUILayout.Label("搜索", GUILayout.Width(45f));
        _encyclopediaSearch = GUILayout.TextField(_encyclopediaSearch);
        if (GUILayout.Button("刷新", GUILayout.Width(60f))) RefreshEncyclopedia(motor);
        GUILayout.EndHorizontal();

        string search = _encyclopediaSearch.Trim();
        List<ResourceData.ConstructionInfo> filteredRecords = _encyclopediaRecords.FindAll(record =>
            string.IsNullOrEmpty(search) ||
            record.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            GetDisplayName(record.Name).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            record.CodeName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);

        GUILayout.Label($"匹配 {filteredRecords.Count} 项");
        _encyclopediaListScrollPos = GUILayout.BeginScrollView(
            _encyclopediaListScrollPos,
            false,
            true,
            GUIStyle.none,
            GUI.skin.verticalScrollbar,
            GUILayout.Height(180f));
        foreach (ResourceData.ConstructionInfo record in filteredRecords)
        {
            bool isSelected = record.CodeName == _selectedConstructionCode;
            Color oldColor = GUI.color;
            GUI.color = isSelected ? new Color(0.72f, 1f, 0.72f) : oldColor;
            if (GUILayout.Button($"{GetDisplayName(record.Name)}  [{record.CodeName}]", GUILayout.ExpandWidth(true)))
            {
                _selectedConstructionCode = record.CodeName;
                _encyclopediaDetailScrollPos = Vector2.zero;
            }
            GUI.color = oldColor;
        }
        GUILayout.EndScrollView();

        ResourceData.ConstructionInfo selectedRecord =
            _encyclopediaRecords.Find(record => record.CodeName == _selectedConstructionCode);
        if (selectedRecord != null)
        {
            GUILayout.BeginVertical("Box");
            GUILayout.Label($"{GetDisplayName(selectedRecord.Name)}  [{selectedRecord.CodeName}]");
            if (!string.IsNullOrEmpty(selectedRecord.Description))
            {
                GUILayout.Label(selectedRecord.Description, GUILayout.ExpandWidth(true));
            }
            else
            {
                GUILayout.Label("原版没有提供可读取的建筑说明。");
            }

            _encyclopediaDetailScrollPos = GUILayout.BeginScrollView(
                _encyclopediaDetailScrollPos,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.Height(220f));
            foreach (ResourceData.ConstructionDetail detail in selectedRecord.Details)
            {
                GUILayout.Label(
                    $"{GetDisplayName(detail.Name)}：{ResourceData.FormatValue(detail.Value, GetDisplayName, _plugin.ControlInstance)}",
                    GUILayout.ExpandWidth(true));
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
        GUILayout.EndVertical();
    }

    private void RefreshEncyclopedia(Motor motor)
    {
        _encyclopediaMotor = motor;
        _encyclopediaControl = _plugin.ControlInstance;
        _encyclopediaRecords = ResourceData.GetConstructionInfo(motor, _plugin.ControlInstance);
        if (_encyclopediaRecords.Find(record => record.CodeName == _selectedConstructionCode) == null)
        {
            _selectedConstructionCode = "";
        }
    }

    private void DrawAchievementUnlocker()
    {
        _foldAchievement = GUILayout.Toggle(_foldAchievement, "【成就解锁】", "Button");
        if (!_foldAchievement) return;

        GUILayout.BeginVertical("Box");
        GUILayout.Label($"当前平台：{AchievementSystem.CurrentPlatform}");
        GUILayout.Label("输入 Steam / Epic 后台定义的成就 ID；提交后可能会同步到账号，且无法通过本面板撤回。");
        GUILayout.Label("游戏 API 不返回解锁结果，请在对应平台确认状态。");
        _achievementId = GUILayout.TextField(_achievementId);

        bool previousGuiEnabled = GUI.enabled;
        GUI.enabled = !string.IsNullOrWhiteSpace(_achievementId) && AchievementSystem.CurrentPlatform != StorePlatform.NONE;
        if (GUILayout.Button("请求解锁成就"))
        {
            string achievementId = _achievementId.Trim();
            AchievementSystem.UnlockAcheivement(achievementId);
            Plugin.Log.LogInfo($"已向 {AchievementSystem.CurrentPlatform} 提交成就解锁请求：{achievementId}。请在平台确认结果。");
        }
        GUI.enabled = previousGuiEnabled;
        GUILayout.EndVertical();
    }

    private void DrawGameInfo(Motor motor)
    {
        List<KeyValuePair<string, string>> info = ResourceData.GetGameInfo(motor);
        if (info.Count == 0) return;

        GUILayout.BeginVertical("Box");
        GUILayout.Label("【游戏状态】");
        for (int i = 0; i < info.Count; i += 2)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{info[i].Key}：{info[i].Value}", GUILayout.ExpandWidth(true));
            if (i + 1 < info.Count)
            {
                GUILayout.Label($"{info[i + 1].Key}：{info[i + 1].Value}", GUILayout.ExpandWidth(true));
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }


    private void EnsureTabState(int tabIndex)
    {
        if (!_selectedKeys.ContainsKey(tabIndex)) _selectedKeys[tabIndex] = null;
        if (!_selectedDictionaries.ContainsKey(tabIndex)) _selectedDictionaries[tabIndex] = null;
        if (!_inputValues.ContainsKey(tabIndex)) _inputValues[tabIndex] = "0";
        if (!_savedNames.ContainsKey(tabIndex)) _savedNames[tabIndex] = "";
        if (!_searchTexts.ContainsKey(tabIndex)) _searchTexts[tabIndex] = "";
    }

    private void DrawDictionaryItems(List<ResourceData.Entry> entries, int tabIndex)
    {
        EnsureTabState(tabIndex);
        GUILayout.BeginHorizontal();
        GUILayout.Label("搜索", GUILayout.Width(45f));
        _searchTexts[tabIndex] = GUILayout.TextField(_searchTexts[tabIndex]);
        if (GUILayout.Button("清除", GUILayout.Width(48f))) _searchTexts[tabIndex] = "";
        GUILayout.EndHorizontal();

        string search = _searchTexts[tabIndex].Trim();
        List<ResourceData.Entry> filteredEntries = entries.FindAll(entry =>
            string.IsNullOrEmpty(search) ||
            entry.KeyName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            GetDisplayPath(entry.KeyName).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            (!string.IsNullOrEmpty(entry.CategoryName) &&
                (entry.CategoryName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 GetDisplayName(entry.CategoryName).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)) ||
            entry.SourceName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            GetDisplayName(entry.SourceName).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
        GUILayout.Label($"共 {filteredEntries.Count} 项");

        _listScrollPos = GUILayout.BeginScrollView(
            _listScrollPos,
            false,
            true,
            GUIStyle.none,
            GUI.skin.verticalScrollbar,
            GUILayout.Height(300f));
        foreach (ResourceData.Entry entry in filteredEntries)
        {
            bool isSelected = ReferenceEquals(entry.Dictionary, _selectedDictionaries[tabIndex]) && Equals(entry.Key, _selectedKeys[tabIndex]);
            Color oldColor = GUI.color;
            GUI.color = isSelected ? new Color(0.72f, 1f, 0.72f) : oldColor;
            string valueText = entry.Value == null
                ? "无数据"
                : Convert.ToString(entry.Value, CultureInfo.CurrentCulture);
            string displayPath = GetDisplayPath(entry.KeyName);
            if (!string.IsNullOrEmpty(entry.CategoryName))
            {
                displayPath = $"{GetDisplayName(entry.CategoryName)} / {displayPath}";
            }
            string displayName = string.IsNullOrEmpty(entry.SourceName)
                ? displayPath
                : $"{GetDisplayName(entry.SourceName)} / {displayPath}";
            if (GUILayout.Button($"{displayName}    =    {valueText}", GUILayout.ExpandWidth(true)))
            {
                _selectedKeys[tabIndex] = entry.Key;
                _selectedDictionaries[tabIndex] = entry.Dictionary;
                _savedNames[tabIndex] = displayName;
                _inputValues[tabIndex] = Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? "0";
            }
            GUI.color = oldColor;
        }
        GUILayout.EndScrollView();
    }

    // ======================= 翻译系统 =======================
    private void ReloadLang()
    {
        _lang.Clear();
        _langLoaded = false;
        EnsureLangLoaded();
    }

    private void EnsureLangLoaded()
    {
        if (_langLoaded) return;
        string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(dllDir)) { _langLoaded = true; return; }

        string path = Path.Combine(dllDir, _langFileName);
        if (!File.Exists(path)) { _langLoaded = true; return; }

        _lang.Clear();
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;

            int idx = trimmed.IndexOf('=');
            if (idx <= 0) idx = trimmed.IndexOf(':'); // 兼容冒号分隔
            if (idx <= 0) continue;

            string key = trimmed.Substring(0, idx).Trim();
            string value = trimmed.Substring(idx + 1).Trim();
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value)) _lang[key] = value;
        }
        _langLoaded = true;
        Plugin.Log.LogInfo($"已加载 {_lang.Count} 条翻译。");
    }

    private string GetDisplayName(string rawKey)
    {
        EnsureLangLoaded();
        if (string.IsNullOrEmpty(rawKey)) return rawKey;
        return _lang.TryGetValue(rawKey, out string zh) && !string.IsNullOrEmpty(zh) ? zh : rawKey;
    }

    private string GetDisplayPath(string rawPath)
    {
        if (string.IsNullOrEmpty(rawPath)) return rawPath;

        string[] parts = rawPath.Split(new[] { '.', '[', ']' }, StringSplitOptions.None);
        var translated = new System.Text.StringBuilder(rawPath.Length + 16);
        int partIndex = 0;
        for (int i = 0; i < rawPath.Length; i++)
        {
            char character = rawPath[i];
            if (character == '.' || character == '[' || character == ']')
            {
                translated.Append(character);
                partIndex++;
            }
            else
            {
                int start = i;
                while (i + 1 < rawPath.Length && rawPath[i + 1] != '.' &&
                    rawPath[i + 1] != '[' && rawPath[i + 1] != ']')
                {
                    i++;
                }
                translated.Append(GetDisplayName(parts[partIndex]));
                if (i < start) i = start;
            }
        }

        return translated.ToString();
    }
}