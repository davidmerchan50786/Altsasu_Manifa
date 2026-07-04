using System;
using System.Collections.Generic;
using System.Text;
using Convai.Domain.Logging;
using Convai.Runtime;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.LoggerSettings
{
    /// <summary>
    ///     Logger Settings section for the Convai Configuration Window.
    ///     Uses the new ConvaiSettings-based logging configuration with LogLevel and LogCategory.
    /// </summary>
    [UxmlElement]
    public partial class ConvaiLoggerSettingSection : ConvaiBaseSection
    {
        /// <summary>UXML section name.</summary>
        public const string SECTION_NAME = "logger-setting";

        private const string InheritColumnKey = "INHERIT";
        private readonly Dictionary<LogCategory, CategoryRowState> _categoryRowStates = new();
        private readonly Dictionary<LogLevel, Button> _globalLevelButtons = new();

        private readonly List<LevelOption> _levelOptions = new()
        {
            new LevelOption(LogLevel.Off, "Off"),
            new LevelOption(LogLevel.Error, "Error"),
            new LevelOption(LogLevel.Warning, "Warn"),
            new LevelOption(LogLevel.Info, "Info"),
            new LevelOption(LogLevel.Debug, "Debug"),
            new LevelOption(LogLevel.Trace, "Trace")
        };

        private readonly LoggerSettingsLogic _loggerSettingsLogic;
        private Toggle _coloredOutputToggle;
        private Toggle _includeStackTracesToggle;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConvaiLoggerSettingSection" /> class.
        /// </summary>
        public ConvaiLoggerSettingSection()
        {
            AddToClassList("section-card");
            Add(ConvaiVisualElementUtility.CreateLabel("header", "Logger Settings", "header"));
            _loggerSettingsLogic = new LoggerSettingsLogic();
            CreateSettingsUI();
        }

        private void CreateSettingsUI()
        {
            var settings = ConvaiSettings.Instance;
            if (settings == null)
            {
                Add(new Label("ConvaiSettings not found. Please create a ConvaiSettings asset."));
                return;
            }

            if (_loggerSettingsLogic.SettingsObject == null)
            {
                Add(new Label("Failed to load ConvaiSettings."));
                return;
            }

            BuildMatrixUI();
            RefreshToolbarState();
            RefreshMatrixState();
        }

        private void BuildMatrixUI()
        {
            var matrixRoot = new VisualElement();
            matrixRoot.AddToClassList("logger-matrix");

            // Toolbar stays outside the hscroll — it wraps on its own
            matrixRoot.Add(CreateToolbar());

            // Header + rows share a horizontal ScrollView so fixed-width columns
            // scroll together rather than overflowing and overlapping other content.
            var hScroll = new ScrollView(ScrollViewMode.Horizontal);
            hScroll.AddToClassList("logger-matrix-hscroll");
            hScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            hScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;

            var matrixContent = new VisualElement();
            matrixContent.style.flexDirection = FlexDirection.Column;
            matrixContent.style.flexGrow = 1;

            matrixContent.Add(CreateMatrixHeader());
            matrixContent.Add(CreateRowScrollRegion());

            hScroll.Add(matrixContent);
            matrixRoot.Add(hScroll);

            Add(matrixRoot);
        }

        private VisualElement CreateToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("logger-toolbar");

            var levelsRow = new VisualElement();
            levelsRow.AddToClassList("logger-toolbar-section");
            levelsRow.AddToClassList("logger-toolbar-section--levels");
            var titleLabel = new Label("Global Log Level & Defaults");
            titleLabel.AddToClassList("logger-toolbar-title");
            levelsRow.Add(titleLabel);

            var segmentedLevels = new VisualElement();
            segmentedLevels.AddToClassList("logger-toolbar-segmented");
            for (int i = 0; i < _levelOptions.Count; i++)
            {
                LevelOption option = _levelOptions[i];
                LogLevel level = option.Level;
                var levelButton = new Button(() =>
                {
                    _loggerSettingsLogic.SetGlobalLogLevel(level);
                    RefreshToolbarState();
                    RefreshMatrixState();
                }) { text = option.Label };
                levelButton.AddToClassList("logger-toolbar-chip");
                if (i == 0)
                    levelButton.AddToClassList("logger-toolbar-chip--first");
                else if (i == _levelOptions.Count - 1)
                    levelButton.AddToClassList("logger-toolbar-chip--last");
                else
                    levelButton.AddToClassList("logger-toolbar-chip--middle");

                _globalLevelButtons[level] = levelButton;
                segmentedLevels.Add(levelButton);
            }

            levelsRow.Add(segmentedLevels);
            toolbar.Add(levelsRow);

            var controlsRow = new VisualElement();
            controlsRow.AddToClassList("logger-toolbar-section");
            controlsRow.AddToClassList("logger-toolbar-section--toggles");

            _includeStackTracesToggle = new Toggle("Include Stack Traces")
            {
                tooltip = "Enable stack traces for Warning and Error logs."
            };
            _includeStackTracesToggle.AddToClassList("logger-toolbar-toggle");
            _includeStackTracesToggle.RegisterValueChangedCallback(evt =>
                _loggerSettingsLogic.SetIncludeStackTraces(evt.newValue));
            controlsRow.Add(_includeStackTracesToggle);

            _coloredOutputToggle = new Toggle("Colored Output")
            {
                tooltip = "Enable colored output in the Unity Console."
            };
            _coloredOutputToggle.AddToClassList("logger-toolbar-toggle");
            _coloredOutputToggle.RegisterValueChangedCallback(evt =>
                _loggerSettingsLogic.SetColoredOutput(evt.newValue));
            controlsRow.Add(_coloredOutputToggle);
            toolbar.Add(controlsRow);

            return toolbar;
        }

        private VisualElement CreateMatrixHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("logger-matrix-header");

            header.Add(CreateHeaderCell("Category", "logger-matrix-cell--category"));
            header.Add(CreateHeaderCell("Effective", "logger-matrix-cell--effective"));
            header.Add(CreateHeaderCell("Inherit", false));
            for (int i = 0; i < _levelOptions.Count; i++)
                header.Add(CreateHeaderCell(_levelOptions[i].Label, i == _levelOptions.Count - 1));

            return header;
        }

        private static Label CreateHeaderCell(string text, string widthClass)
        {
            var headerCell = new Label(text);
            headerCell.AddToClassList("logger-matrix-cell");
            headerCell.AddToClassList("logger-matrix-header-cell");
            headerCell.AddToClassList(widthClass);
            return headerCell;
        }

        private static Label CreateHeaderCell(string text, bool isLastOption)
        {
            var headerCell = new Label(text);
            headerCell.AddToClassList("logger-matrix-cell");
            headerCell.AddToClassList("logger-matrix-header-cell");
            headerCell.AddToClassList("logger-matrix-cell--option");
            if (isLastOption) headerCell.AddToClassList("logger-matrix-cell--option-last");
            return headerCell;
        }

        private VisualElement CreateRowScrollRegion()
        {
            // Plain container — vertical scrolling is handled by the section-level ScrollView
            // in ConvaiContentContainerVisualElement, so we don't need a nested one here.
            var rowsContainer = new VisualElement();
            rowsContainer.AddToClassList("logger-scroll-region");

            var categories = (LogCategory[])Enum.GetValues(typeof(LogCategory));
            Array.Sort(categories, (a, b) =>
                string.Compare(GetCategoryDisplayName(a), GetCategoryDisplayName(b),
                    StringComparison.OrdinalIgnoreCase));
            foreach (LogCategory category in categories)
                rowsContainer.Add(CreateCategoryRow(category));

            return rowsContainer;
        }

        private VisualElement CreateCategoryRow(LogCategory category)
        {
            var row = new VisualElement();
            row.AddToClassList("logger-matrix-row");

            var categoryCell = new VisualElement();
            categoryCell.AddToClassList("logger-matrix-cell");
            categoryCell.AddToClassList("logger-matrix-cell--category");
            var categoryLabel = new Label(GetCategoryDisplayName(category));
            categoryLabel.AddToClassList("logger-category-label");
            categoryCell.Add(categoryLabel);
            row.Add(categoryCell);

            var effectiveCell = new VisualElement();
            effectiveCell.AddToClassList("logger-matrix-cell");
            effectiveCell.AddToClassList("logger-matrix-cell--effective");
            effectiveCell.AddToClassList("logger-effective-badge");
            var effectiveDot = new VisualElement();
            effectiveDot.AddToClassList("logger-effective-dot");
            var effectiveLabel = new Label();
            effectiveLabel.AddToClassList("logger-effective-text");
            effectiveCell.Add(effectiveDot);
            effectiveCell.Add(effectiveLabel);
            row.Add(effectiveCell);

            var selectionButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
            selectionButtons[InheritColumnKey] = AddSelectionButton(
                row,
                "Inherit",
                false,
                () =>
                {
                    _loggerSettingsLogic.SetCategoryOverride(category, null);
                    RefreshCategoryRow(category);
                });

            for (int i = 0; i < _levelOptions.Count; i++)
            {
                LevelOption option = _levelOptions[i];
                LogLevel level = option.Level;
                selectionButtons[GetLevelKey(level)] = AddSelectionButton(
                    row,
                    option.Label,
                    i == _levelOptions.Count - 1,
                    () =>
                    {
                        _loggerSettingsLogic.SetCategoryOverride(category, level);
                        RefreshCategoryRow(category);
                    });
            }

            _categoryRowStates[category] = new CategoryRowState(row, effectiveDot, effectiveLabel, selectionButtons);
            return row;
        }

        private Button AddSelectionButton(VisualElement row, string label, bool isLastOption, Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("logger-matrix-cell");
            button.AddToClassList("logger-matrix-cell--option");
            if (isLastOption) button.AddToClassList("logger-matrix-cell--option-last");
            button.AddToClassList("logger-matrix-option");
            row.Add(button);
            return button;
        }


        private void RefreshToolbarState()
        {
            LogLevel globalLevel = _loggerSettingsLogic.GetGlobalLogLevel();
            foreach (KeyValuePair<LogLevel, Button> pair in _globalLevelButtons)
                pair.Value.EnableInClassList("logger-toolbar-chip--selected", pair.Key == globalLevel);

            _includeStackTracesToggle?.SetValueWithoutNotify(_loggerSettingsLogic.GetIncludeStackTraces());
            _coloredOutputToggle?.SetValueWithoutNotify(_loggerSettingsLogic.GetColoredOutput());
        }

        /// <summary>
        ///     Refreshes all category rows. Used after bulk operations (global level change, Set All, Inherit All, Reset).
        /// </summary>
        private void RefreshMatrixState()
        {
            IReadOnlyDictionary<LogCategory, LogLevel> overrides =
                _loggerSettingsLogic.GetCategoryOverridesSnapshot();
            LogLevel globalLevel = _loggerSettingsLogic.GetGlobalLogLevel();

            foreach (KeyValuePair<LogCategory, CategoryRowState> rowPair in _categoryRowStates)
            {
                bool hasOverride = overrides.TryGetValue(rowPair.Key, out LogLevel overrideLevel);
                LogLevel effectiveLevel = hasOverride ? overrideLevel : globalLevel;
                string selectedKey = hasOverride ? GetLevelKey(overrideLevel) : InheritColumnKey;

                UpdateRowSelection(rowPair.Value.SelectionButtons, selectedKey);
                UpdateEffectiveBadge(rowPair.Value, effectiveLevel, hasOverride, globalLevel);
            }
        }

        /// <summary>
        ///     Refreshes a single category row. Used after per-category override changes to avoid
        ///     iterating all rows (~221 class mutations) when only one row changed.
        /// </summary>
        private void RefreshCategoryRow(LogCategory category)
        {
            if (!_categoryRowStates.TryGetValue(category, out CategoryRowState rowState)) return;

            LogLevel? overrideLevel = _loggerSettingsLogic.GetCategoryOverride(category);
            LogLevel globalLevel = _loggerSettingsLogic.GetGlobalLogLevel();
            bool hasOverride = overrideLevel.HasValue;
            LogLevel effectiveLevel = hasOverride ? overrideLevel.Value : globalLevel;
            string selectedKey = hasOverride ? GetLevelKey(overrideLevel.Value) : InheritColumnKey;

            UpdateRowSelection(rowState.SelectionButtons, selectedKey);
            UpdateEffectiveBadge(rowState, effectiveLevel, hasOverride, globalLevel);
        }

        private static void UpdateRowSelection(Dictionary<string, Button> selectionButtons, string selectedKey)
        {
            foreach (KeyValuePair<string, Button> pair in selectionButtons)
            {
                pair.Value.EnableInClassList("logger-matrix-cell--selected",
                    string.Equals(pair.Key, selectedKey, StringComparison.Ordinal));
            }
        }

        private static void UpdateEffectiveBadge(
            CategoryRowState rowState,
            LogLevel effectiveLevel,
            bool hasOverride,
            LogLevel globalLevel)
        {
            SetEffectiveDotLevel(rowState.EffectiveDot, effectiveLevel);
            rowState.EffectiveText.text = hasOverride
                ? GetVerboseLevelLabel(effectiveLevel)
                : $"Global: {GetVerboseLevelLabel(globalLevel)}";
            rowState.EffectiveText.tooltip = hasOverride
                ? $"Category override is set to {GetVerboseLevelLabel(effectiveLevel)}."
                : $"No override. Inheriting global level {GetVerboseLevelLabel(globalLevel)}.";
        }

        private static void SetEffectiveDotLevel(VisualElement dot, LogLevel level)
        {
            dot.EnableInClassList("logger-effective-dot--off", level == LogLevel.Off);
            dot.EnableInClassList("logger-effective-dot--error", level == LogLevel.Error);
            dot.EnableInClassList("logger-effective-dot--warning", level == LogLevel.Warning);
            dot.EnableInClassList("logger-effective-dot--info", level == LogLevel.Info);
            dot.EnableInClassList("logger-effective-dot--debug", level == LogLevel.Debug);
            dot.EnableInClassList("logger-effective-dot--trace", level == LogLevel.Trace);
        }

        private static string GetLevelKey(LogLevel level) => $"LEVEL_{(int)level}";

        private static string GetVerboseLevelLabel(LogLevel level) =>
            level == LogLevel.Warning ? "Warning" : level.ToString();

        private static string GetCategoryDisplayName(LogCategory category)
        {
            string raw = category.ToString();
            if (string.Equals(raw, raw.ToUpperInvariant(), StringComparison.Ordinal)) return raw;

            var builder = new StringBuilder(raw.Length + 4);
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1])) builder.Append(' ');
                builder.Append(raw[i]);
            }

            return builder.ToString();
        }

        private sealed class LevelOption
        {
            public LevelOption(LogLevel level, string label)
            {
                Level = level;
                Label = label;
            }

            public LogLevel Level { get; }
            public string Label { get; }
        }

        private sealed class CategoryRowState
        {
            public CategoryRowState(
                VisualElement root,
                VisualElement effectiveDot,
                Label effectiveText,
                Dictionary<string, Button> selectionButtons)
            {
                Root = root;
                EffectiveDot = effectiveDot;
                EffectiveText = effectiveText;
                SelectionButtons = selectionButtons;
            }

            public VisualElement Root { get; }
            public VisualElement EffectiveDot { get; }
            public Label EffectiveText { get; }
            public Dictionary<string, Button> SelectionButtons { get; }
        }
    }
}
