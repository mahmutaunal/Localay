using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Control = System.Windows.Controls.Control;
using Panel = System.Windows.Controls.Panel;
using Brushes = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace AlpWareStudio.Localay;

public partial class SettingsWindow
{
    private ResultPreferenceDraft? _resultDraft;
    private bool _syncingResultDraft;
    private bool _savingResultDraft;
    private string? _resultDraftStatus;
    private string? _resultPreviewIssue;
    private Action? _pendingResultAction;
    private readonly Dictionary<string, Control> _resultInputs = new();
    private readonly Dictionary<string, FrameworkElement> _resultInputRows = new();

    private void InitializeResultEditor()
    {
        AddResultInput(resultOverlayForm, "language_font_override", "Draft_ResultOverrideFont", "bool");
        AddResultInput(resultOverlayForm, "language_font_family", "Draft_ResultFont", "font");
        AddResultInput(resultOverlayForm, "language_font_size_min", "Draft_ResultMinSize", "text");
        AddResultInput(resultOverlayForm, "language_font_size_max", "Draft_ResultMaxSize", "text");
        AddResultInput(resultOverlayForm, "OverlayTextColor", "Draft_ResultTextColor", "color");
        AddResultInput(resultOverlayForm, "IsAutoSetOverlayBackground", "Draft_ResultAutoBackground", "bool");
        AddResultInput(resultOverlayForm, "OverlayBackgroundColor", "Draft_ResultBackground", "color");
        AddResultInput(resultOverlayBehaviorForm, "show_icon_signal", "Lbl_ShowIconSignal", "bool");
        AddResultInput(resultOverlayBehaviorForm, "AutoMergeOverlappingText", "Lbl_AutoMergeOverlappingText", "bool");
        AddResultInput(resultOverlayBehaviorForm, "leave_translation_onscreen", "Lbl_LeaveOnscreen", "bool");
        AddResultInput(resultChatForm, "chatbox_font_family", "Draft_ResultFont", "font");
        AddResultInput(resultChatForm, "chatbox_font_size", "Draft_ResultChatSize", "text");
        AddResultInput(resultChatForm, "chatbox_original_text_color", "Draft_ResultOriginalColor", "color");
        AddResultInput(resultChatForm, "chatbox_translated_text_color", "Draft_ResultTextColor", "color");
        AddResultInput(resultChatForm, "chatbox_text_outline_color", "Draft_ResultOutlineColor", "color");
        AddResultInput(resultChatForm, "chatbox_background_color", "Draft_ResultBackground", "color");
        AddResultInput(resultChatForm, "chatbox_background_opacity", "Draft_ResultBackgroundOpacity", "text");
        AddResultInput(resultChatForm, "chatbox_window_opacity", "Draft_ResultWindowOpacity", "text");
        AddResultInput(resultChatBehaviorForm, "auto_clear_chat_history", "Draft_ResultClearOnStop", "bool");
        AddResultInput(resultChatBehaviorForm, "auto_clear_chat_timeout", "Draft_ResultClearTimeout", "text");
        AddResultInput(resultChatBehaviorForm, "chatbox_recreate_on_show", "Draft_ResultRecreate", "bool");
        _resultInputRows["IsAutoSetOverlayBackground"].Visibility = Visibility.Collapsed;
    }

    private void AddResultInput(Panel parent, string key, string labelKey, string kind)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        UiText.Bind(label, TextBlock.TextProperty, labelKey);
        Control input;
        if (kind == "bool")
        {
            var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            check.Checked += ResultInput_Changed; check.Unchecked += ResultInput_Changed;
            input = check;
        }
        else if (kind == "font")
        {
            var combo = new ComboBox { IsEditable = true, MinHeight = 34 };
            if (_initializeRuntime) foreach (string name in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n)) combo.Items.Add(name);
            combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ResultInputEdited(combo)), true);
            combo.SelectionChanged += (_, _) =>
            {
                if (_syncingResultDraft || _isInitializing) return;
                if (combo.SelectedItem is string selected) combo.Text = selected;
                ResultInputEdited(combo);
            };
            input = combo;
        }
        else
        {
            var text = new TextBox { MinHeight = 34, Padding = new Thickness(8, 5, 8, 5) };
            text.TextChanged += (_, _) => ResultInputEdited(text);
            input = text;
        }
        input.Tag = key;
        System.Windows.Automation.AutomationProperties.SetLabeledBy(input, label);
        input.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        if (kind != "bool") input.SetResourceReference(Control.BackgroundProperty, "Surface2Brush");
        _resultInputs.Add(key, input);
        row.Children.Add(label);
        var editor = new StackPanel();
        editor.Children.Add(input);
        if (kind == "color")
        {
            var palette = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (string hex in new[] { "#FF000000", "#FFFFFFFF", "#FFF8E0A0", "#FF9CC2FF", "#FF8AD9AF" })
            {
                var button = new Button { Width = 28, Height = 28, MinWidth = 0, Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    BorderThickness = new Thickness(1), ToolTip = hex };
                System.Windows.Automation.AutomationProperties.SetName(button, hex);
                button.Click += (_, _) => { if (input is TextBox text) text.Text = hex; };
                palette.Children.Add(button);
            }
            editor.Children.Add(palette);
        }
        Grid.SetColumn(editor, 1); row.Children.Add(editor);
        _resultInputRows.Add(key, editor);
        parent.Children.Add(row);
    }

    private void ResultInput_Changed(object sender, RoutedEventArgs e) => ResultInputEdited(sender);
    private void ResultInputEdited(object sender)
    {
        if (_isInitializing || _syncingResultDraft || _resultDraft == null || sender is not Control input || input.Tag is not string key) return;
        string value = input switch { CheckBox check => check.IsChecked == true ? "true" : "false", ComboBox combo => combo.Text, TextBox text => text.Text, _ => "" };
        _resultDraft.Set(key, value); _resultDraftStatus = null;
        RenderResultDraft();
        if (!_resultDraft.IsDirty) CompleteResultAction();
    }

    private void RefreshResultDraft()
    {
        if (!_initializeRuntime || _savingResultDraft) return;
        var current = ConfigManager.Instance.ReadResultPreferences();
        if (_resultDraft == null) _resultDraft = new(current);
        else _resultDraft.Observe(current);
        _syncingResultDraft = true;
        try
        {
            foreach (var field in _resultInputs)
            {
                string value = _resultDraft[field.Key];
                switch (field.Value)
                {
                    case CheckBox check: check.IsChecked = value == "true"; break;
                    case ComboBox combo: combo.Text = value; break;
                    case TextBox text: text.Text = value; break;
                }
            }
        }
        finally { _syncingResultDraft = false; }
        RenderResultDraft();
    }

    private void UpdateResultPreview(IReadOnlyDictionary<string, string> values)
    {
        var overlayFont = new FontFamily(values["language_font_override"] == "true" ? values["language_font_family"] : "Segoe UI");
        var chatFont = new FontFamily(values["chatbox_font_family"]);
        double Number(string key) => double.Parse(values[key], CultureInfo.InvariantCulture);
        SolidColorBrush Brush(string key) => new((Color)ColorConverter.ConvertFromString(values[key]));
        var gameBackground = (Color)ColorConverter.ConvertFromString(values["OverlayBackgroundColor"]);
        overlayPreviewSurface.Background = new SolidColorBrush(Color.FromRgb(gameBackground.R, gameBackground.G, gameBackground.B));
        overlayPreviewText.Foreground = Brush("OverlayTextColor");
        overlayPreviewText.FontFamily = overlayFont;
        overlayPreviewText.FontSize = Math.Clamp(24, Number("language_font_size_min"), Number("language_font_size_max"));
        var background = (Color)ColorConverter.ConvertFromString(values["chatbox_background_color"]);
        byte alpha = (byte)(ResultPreferenceDraft.ChatBackgroundOpacity(Number("chatbox_background_opacity")) * 255);
        chatPreviewSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B));
        chatPreviewTextSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        chatPreviewSurface.Opacity = Number("chatbox_window_opacity");
        foreach (var text in new[] { chatPreviewOriginal, chatPreviewTranslation })
        {
            text.FontFamily = chatFont;
            text.FontSize = Number("chatbox_font_size");
            text.Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 5, Color = ((SolidColorBrush)Brush("chatbox_text_outline_color")).Color, Opacity = 1 };
        }
        chatPreviewOriginal.Foreground = Brush("chatbox_original_text_color");
        chatPreviewTranslation.Foreground = Brush("chatbox_translated_text_color");
    }

    private void RenderResultDraft()
    {
        if (_resultDraft == null) return;
        var candidate = _resultDraft.Candidate();
        string? issue = ResultPreferenceDraft.Validate(candidate);
        _resultPreviewIssue = null;
        if (issue == null)
        {
            try { UpdateResultPreview(candidate); }
            catch (ArgumentException) { _resultPreviewIssue = "Draft_ResultFontInvalid"; }
            catch (FormatException) { _resultPreviewIssue = "Draft_ResultInvalid"; }
        }
        issue ??= _resultPreviewIssue;
        bool conflict = _resultDraft.HasConflict(ConfigManager.Instance.ReadResultPreferences());
        _resultInputRows["OverlayBackgroundColor"].IsEnabled = true;
        _resultInputRows["OverlayTextColor"].IsEnabled = true;
        _resultInputRows["language_font_family"].IsEnabled = _resultDraft["language_font_override"] == "true";
        string status = conflict ? "Draft_ResultConflict" : _pendingResultAction != null ? "Draft_ResultDecision"
            : issue ?? _resultDraftStatus ?? (_resultDraft.IsDirty ? "Draft_ResultDirty" : "Draft_ResultClean");
        UiText.Bind(resultDraftStatusText, TextBlock.TextProperty, status);
        resultDraftStatusText.SetResourceReference(TextBlock.ForegroundProperty, issue != null || conflict ? "WarningBrush" : "MutedBrush");
        saveResultDraftButton.IsEnabled = _resultDraft.IsDirty && !conflict && issue == null && !_savingResultDraft;
        cancelResultDraftButton.IsEnabled = _resultDraft.IsDirty && !_savingResultDraft;
        stayWithResultDraftButton.Visibility = _pendingResultAction != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveResultDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_resultDraft == null || !_resultDraft.IsDirty || _savingResultDraft) return;
        var candidate = _resultDraft.Candidate();
        if (ResultPreferenceDraft.Validate(candidate) != null || _resultPreviewIssue != null) return;
        bool saved = false; _savingResultDraft = true;
        try
        {
            var before = _resultDraft.Baseline;
            saved = ConfigManager.Instance.TrySaveResultPreferences(before, candidate, out bool conflict);
            if (saved)
            {
                _resultDraft.Reset(ConfigManager.Instance.ReadResultPreferences());
                _resultDraftStatus = "Draft_ResultSaved";
                MainWindow.ResultPreferencesCommitted(before, candidate);
            }
            else _resultDraftStatus = conflict ? "Draft_ResultConflict" : "Draft_ResultFailed";
        }
        finally { _savingResultDraft = false; RefreshResultDraft(); }
        if (saved) CompleteResultAction();
    }

    private void CancelResultDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_resultDraft == null || _savingResultDraft) return;
        _resultDraft.Reset(ConfigManager.Instance.ReadResultPreferences()); _resultDraftStatus = "Draft_ResultCancelled";
        RefreshResultDraft(); CompleteResultAction();
    }
    private void ResultDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_resultDraft == null || _savingResultDraft) return;
        _resultDraft.UseDefaults(); _resultDraftStatus = null; RefreshResultDraft();
        if (!_resultDraft.IsDirty) CompleteResultAction();
    }
    private void StayWithResultDraft_Click(object sender, RoutedEventArgs e)
    {
        _pendingResultAction = null; _resultDraftStatus = null; RenderResultDraft();
    }
    private void RequestResultDecision(Action action)
    {
        if (_resultDraft?.IsDirty != true) { action(); return; }
        _pendingResultAction = action; SelectSettingsCategory(resultSettingsCategory); RenderResultDraft();
        if (saveResultDraftButton.IsEnabled) saveResultDraftButton.Focus(); else cancelResultDraftButton.Focus();
    }
    private void CompleteResultAction()
    {
        var action = _pendingResultAction; _pendingResultAction = null; RenderResultDraft(); action?.Invoke();
    }
    internal void ShowResultFor(Window owner)
    {
        ShowFor(owner);
        if (ReferenceEquals(settingsCategories.SelectedItem, resultSettingsCategory)) return;
        RequestSettingsDecision(() => SelectSettingsCategory(resultSettingsCategory));
    }
}
